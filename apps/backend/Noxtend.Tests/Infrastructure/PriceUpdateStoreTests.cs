using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Common;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Tests.Api;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
public sealed class PriceUpdateStoreTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(PriceUpdateStoreTests));
    private readonly AcceptanceClock clock = new();

    [Fact]
    public async Task ConcurrentSameRequest_ReplaysOneReceiptAndSurvivesFreshContext()
    {
        var preview = Preview("same-request");
        await Initialize(preview);
        var input = new PriceUpdateApplyInput(Guid.NewGuid(), preview.Candidates.Select(c => c.Id).ToArray(), null);
        async Task<PriceUpdateReceipt> Apply()
        {
            await using var db = Context();
            var result = await new EfPriceUpdateStore(db, clock).ApplyAsync(preview.Id, input, default);
            Assert.True(result.IsSuccess, result.ErrorMessage);
            return result.Value!;
        }
        var results = await Task.WhenAll(Apply(), Apply());
        Assert.Equal(results[0].Items.Single().PriceId, results[1].Items.Single().PriceId);
        clock.Advance(TimeSpan.FromHours(1));
        var replay = await Apply();
        Assert.Equal(results[0].AppliedAt, replay.AppliedAt);
        await using var check = Context();
        Assert.Single(await check.ModelPrices.ToListAsync());
        Assert.Single(await check.Set<PriceUpdateRequestReceipt>().ToListAsync());
    }

    [Fact]
    public async Task ReorderedCandidatesAndNormalizedUtc_ReplayOriginalReceipt()
    {
        var preview = Preview("ordered-a", "ordered-b");
        await Initialize(preview);
        var effective = clock.GetUtcNow().AddHours(2);
        var input = new PriceUpdateApplyInput(Guid.NewGuid(), preview.Candidates.Select(c => c.Id).ToArray(), effective);
        await using (var db = Context())
            Assert.True((await new EfPriceUpdateStore(db, clock).ApplyAsync(preview.Id, input, default)).IsSuccess);
        clock.Advance(TimeSpan.FromHours(1));
        await using var replayDb = Context();
        var replay = await new EfPriceUpdateStore(replayDb, clock).ApplyAsync(preview.Id,
            input with { CandidateIds = input.CandidateIds!.Reverse().ToArray(), EffectiveFrom = effective.ToOffset(TimeSpan.FromHours(9)) }, default);
        Assert.True(replay.IsSuccess, replay.ErrorMessage);
        Assert.All(replay.Value!.Items, i => Assert.Equal(effective, i.EffectiveFrom));
        Assert.Equal(2, await replayDb.ModelPrices.CountAsync());
        var conflict = await new EfPriceUpdateStore(replayDb, clock).ApplyAsync(Guid.NewGuid(), input, default);
        Assert.Equal(ErrorCode.PriceUpdateRequestConflict, conflict.ErrorCode);
    }

    [Fact]
    public async Task DuplicateTargetModels_RejectAllWrites()
    {
        var preview = Preview("duplicate-target", "duplicate-target");
        await Initialize(preview);
        await using var db = Context();
        var result = await new EfPriceUpdateStore(db, clock).ApplyAsync(preview.Id,
            new(Guid.NewGuid(), preview.Candidates.Select(c => c.Id).ToArray(), null), default);
        Assert.Equal(ErrorCode.PriceUpdateInvalid, result.ErrorCode);
        Assert.Empty(await db.ModelPrices.ToListAsync());
        Assert.Empty(await db.Set<PriceUpdateRequestReceipt>().ToListAsync());
    }

    [Fact]
    public async Task SqlFailureDuringInsert_RollsBackPricesAndReceipt()
    {
        var preview = Preview("rollback-a", "rollback-b");
        await Initialize(preview);
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER PriceUpdateFailure ON ModelPrices AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE Model = 'rollback-b') THROW 51001, 'Synthetic rollback check', 1; END");
            await Assert.ThrowsAsync<DbUpdateException>(() => new EfPriceUpdateStore(db, clock).ApplyAsync(preview.Id,
                new(Guid.NewGuid(), preview.Candidates.Select(c => c.Id).ToArray(), null), default));
        }
        await using var check = Context();
        Assert.Empty(await check.ModelPrices.ToListAsync());
        Assert.Empty(await check.Set<PriceUpdateRequestReceipt>().ToListAsync());
    }

    private async Task Initialize(PriceUpdatePreview preview)
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        await db.ModelPrices.ExecuteDeleteAsync();
        await new EfPriceUpdateStore(db, clock).SavePreviewAsync(preview, default);
    }

    private PriceUpdatePreview Preview(params string[] models)
        => new(Guid.NewGuid(), clock.GetUtcNow(), clock.GetUtcNow().AddMinutes(30), [], models.Select(model =>
            new PriceUpdateCandidate(Guid.NewGuid(), "openai", model, "text", "text", "Synthetic rollback/replay test",
                [], null, new OfficialPriceTerms(2m, 4m), [], "newModel", "unverified", null,
                PriceCandidateComparison.Fingerprint("openai", model, []))).ToArray());

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>().UseSqlServer(connectionString,
            options => options.EnableRetryOnFailure()).Options);
}
