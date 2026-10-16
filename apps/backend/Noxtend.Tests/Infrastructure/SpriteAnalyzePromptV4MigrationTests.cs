using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
public sealed class SpriteAnalyzePromptV4MigrationTests(SqlServerFixture sql)
{
    private const string Before = "20261014083000_ReviseSpriteGeneratePrompt";
    private const string Current = "20261015080000_ReviseSpriteAnalyzePromptV4";
    private const string V3Id = "c7000000-0000-4000-8000-000000000003";
    private const string V4Id = "c7000000-0000-4000-8000-000000000004";
    private readonly string connectionString = sql.FreshDatabase(nameof(SpriteAnalyzePromptV4MigrationTests));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpDown_ReplacesOnlyDefaultV3AndRestoresItDespiteHigherInactiveVersion(bool inactiveOperator)
    {
        await using var db = Context();
        await db.Database.MigrateAsync(Before);
        var before = await db.PromptVersions.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        if (inactiveOperator) await InsertOperator(db, false);
        await db.Database.MigrateAsync(Current);
        var active = Assert.Single(await Active(db));
        Assert.Equal(Guid.Parse(V4Id), active.Id);
        Assert.Equal(inactiveOperator ? 10 : 4, active.Version);
        Assert.Equal(SeedPrompts.AnalyzeSpritesV4().Schema, active.JsonSchema);
        Assert.Equal(SeedPrompts.AnalyzeSpritesV4().System, active.System);
        Assert.Equal(SeedPrompts.AnalyzeSpritesV4().User, active.User);
        Assert.Equal(SeedPrompts.AnalyzeSpritesV4().Note, active.Note);
        foreach (var original in before)
        {
            var row = await db.PromptVersions.AsNoTracking().SingleAsync(p => p.Id == original.Id);
            Assert.Equal(original.JsonSchema, row.JsonSchema);
            Assert.Equal(original.Version, row.Version);
            Assert.Equal(original.System, row.System);
            Assert.Equal(original.User, row.User);
            Assert.Equal(original.Note, row.Note);
            Assert.Equal(original.CreatedAt, row.CreatedAt);
            Assert.Equal(original.Id == Guid.Parse(V3Id) ? false : original.IsActive, row.IsActive);
        }
        await db.Database.MigrateAsync(Before);
        Assert.Equal(Guid.Parse(V3Id), Assert.Single(await Active(db)).Id);
        Assert.False(await db.PromptVersions.AnyAsync(p => p.Id == Guid.Parse(V4Id)));
        if (inactiveOperator) Assert.False((await db.PromptVersions.AsNoTracking().SingleAsync(p => p.Version == 9 && p.Kind == LlmOperationKind.AnalyzeSprites)).IsActive);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("modifiedSeed")]
    [InlineData("modifiedSeedTrailing")]
    [InlineData("noActive")]
    public async Task UpDown_SkipsOperatorOrMissingDefaultWithoutChangingRows(string scenario)
    {
        await using var db = Context();
        await db.Database.MigrateAsync(Before);
        if (scenario == "operator") await InsertOperator(db, true);
        else if (scenario == "modifiedSeedTrailing")
            await db.Database.ExecuteSqlRawAsync("UPDATE PromptVersions SET [System] = [System] + N' ' WHERE Id = {0}", Guid.Parse(V3Id));
        else if (scenario == "modifiedSeed")
            await db.Database.ExecuteSqlRawAsync("UPDATE PromptVersions SET [System] = N'operator edit' WHERE Id = {0}", Guid.Parse(V3Id));
        else await db.Database.ExecuteSqlRawAsync("UPDATE PromptVersions SET IsActive = 0 WHERE Id = {0}", Guid.Parse(V3Id));
        var before = await db.PromptVersions.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        await db.Database.MigrateAsync(Current);
        Assert.False(await db.PromptVersions.AnyAsync(p => p.Id == Guid.Parse(V4Id)));
        await db.Database.MigrateAsync(Before);
        var after = await db.PromptVersions.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(before.Select(p => (p.Id, p.Version, p.IsActive, p.System, p.User, p.JsonSchema, p.Note)),
            after.Select(p => (p.Id, p.Version, p.IsActive, p.System, p.User, p.JsonSchema, p.Note)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Down_PreservesOperatorActivationOrDeactivationAfterUp(bool activateOperator)
    {
        await using var db = Context();
        await db.Database.MigrateAsync(Current);
        if (activateOperator) await InsertOperator(db, true);
        else await db.Database.ExecuteSqlRawAsync("UPDATE PromptVersions SET IsActive = 0 WHERE Id = {0}", Guid.Parse(V4Id));
        await db.Database.MigrateAsync(Before);
        Assert.False(await db.PromptVersions.AnyAsync(p => p.Id == Guid.Parse(V4Id)));
        if (activateOperator) Assert.Equal(9, Assert.Single(await Active(db)).Version);
        else Assert.Empty(await Active(db));
    }

    [Fact]
    public async Task UpDown_RestoresSeedV3AllocatedAfterEarlierOperatorVersion()
    {
        await using var db = Context();
        await db.Database.MigrateAsync("20261014064731_AddSpriteAssetRoles");
        await InsertOperator(db, true);
        await db.Database.MigrateAsync(Before);
        Assert.Equal(10, Assert.Single(await Active(db)).Version);
        await db.Database.MigrateAsync(Current);
        Assert.Equal(11, Assert.Single(await Active(db)).Version);
        await db.Database.MigrateAsync(Before);
        var restored = Assert.Single(await Active(db));
        Assert.Equal(Guid.Parse(V3Id), restored.Id);
        Assert.Equal(10, restored.Version);
    }

    [Fact]
    public async Task FreshDatabase_EndsWithSupportedV4Schema()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var prompt = Assert.Single(await Active(db));
        Assert.Equal(SeedPrompts.AnalyzeSpritesV4().Schema, prompt.JsonSchema);
        Assert.DoesNotContain("\"oneOf\"", prompt.JsonSchema);
    }

    private static Task<List<PromptVersion>> Active(NoxtendDbContext db)
        => db.PromptVersions.AsNoTracking().Where(p => p.Kind == LlmOperationKind.AnalyzeSprites &&
            p.Category == AssetCategory.Background && p.IsActive).ToListAsync();

    private static async Task InsertOperator(NoxtendDbContext db, bool active)
    {
        if (active) await db.Database.ExecuteSqlRawAsync("UPDATE PromptVersions SET IsActive = 0 WHERE Kind = 'AnalyzeSprites' AND Category = 'Background'");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO PromptVersions (Id, Kind, Category, Version, [System], [User], JsonSchema, Note, IsActive, CreatedAt) VALUES ({0}, 'AnalyzeSprites', 'Background', 9, 'operator system', 'operator user', '{{}}', 'operator row', {1}, {2})",
            Guid.NewGuid(), active, DateTimeOffset.UtcNow);
    }

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>().UseSqlServer(connectionString, o => o.EnableRetryOnFailure()).Options);
}
