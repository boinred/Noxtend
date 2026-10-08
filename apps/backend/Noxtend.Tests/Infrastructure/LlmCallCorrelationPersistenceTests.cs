using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Infrastructure;

[Collection(SqlServerCollection.Name)]
public sealed class LlmCallCorrelationPersistenceTests(SqlServerFixture sql)
{
    private readonly string connectionString = sql.FreshDatabase(nameof(LlmCallCorrelationPersistenceTests));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceGeneration_RoundTripsRecorderCorrelationAndUsage(bool succeeded)
    {
        await Migrate();
        var sourceGenerationId = Guid.NewGuid();
        var context = LlmCallContext.ForSourceGeneration(sourceGenerationId, Guid.NewGuid(), Guid.NewGuid(), "image-model");
        using (var calls = new EfLlmCallRepository(new ContextFactory(connectionString)))
        {
            var recorder = new TuningLlmCallRecorder(calls, FixedClock.Default);
            await recorder.RecordAsync(new LlmCallEntry(context, "prompt", succeeded ? "image/png" : null,
                succeeded ? 10 : null, succeeded ? 20 : null, 100, succeeded,
                succeeded ? null : "failure", succeeded ? 1 : null), CancellationToken.None);
        }

        await using var read = Context();
        var call = Assert.Single(await read.LlmCalls.ToListAsync());
        Assert.Null(call.JobId);
        Assert.Null(call.TaskId);
        Assert.Null(call.SimilarityEvaluationId);
        Assert.Equal(sourceGenerationId, call.SourceGenerationId);
        Assert.Equal(LlmOperationKind.GenerateSpriteSource, call.Kind);
        Assert.Equal(context.PromptVersionId, call.PromptVersionId);
        Assert.Equal(context.ProviderConfigId, call.ProviderConfigId);
        Assert.Equal(succeeded, call.Succeeded);
        Assert.Equal(succeeded ? 10 : null, call.InputTokens);
        Assert.Equal(succeeded ? 20 : null, call.OutputTokens);
        Assert.Equal(succeeded ? 1 : null, call.OutputImages);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public async Task InvalidCorrelation_IsRejectedBySql(bool hasJob, bool hasTask, bool hasSource)
    {
        await Migrate();
        await using var db = Context();
        db.LlmCalls.Add(Call(hasJob ? Guid.NewGuid() : null, hasTask ? Guid.NewGuid() : null,
            hasSource ? Guid.NewGuid() : null));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StatisticsIncludeSourceCalls_WhileJobHistoryIncludesOnlyJobCalls()
    {
        await Migrate();
        var jobId = Guid.NewGuid();
        var taskCall = Call(jobId, Guid.NewGuid(), null);
        using var calls = new EfLlmCallRepository(new ContextFactory(connectionString));
        await calls.AddAsync(taskCall, CancellationToken.None);
        await calls.AddAsync(Call(null, null, Guid.NewGuid()), CancellationToken.None);
        await calls.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2, (await calls.GetStatsAsync(CancellationToken.None)).Count);
        Assert.Equal(taskCall.Id, Assert.Single(await calls.ListByJobAsync(jobId, CancellationToken.None)).Id);
    }

    private static LlmCall Call(Guid? jobId, Guid? taskId, Guid? sourceGenerationId)
        => LlmCall.Success(jobId, taskId, null,
            sourceGenerationId is null ? LlmOperationKind.Generate : LlmOperationKind.GenerateSpriteSource,
            Guid.NewGuid(), Guid.NewGuid(), "image-model", "prompt", "image/png", 10, 20, 100,
            FixedClock.Default.Now, 1, sourceGenerationId);

    private async Task Migrate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    private NoxtendDbContext Context() => new ContextFactory(connectionString).CreateDbContext();

    private sealed class ContextFactory(string connectionString) : IDbContextFactory<NoxtendDbContext>
    {
        public NoxtendDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<NoxtendDbContext>()
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()).Options);
    }
}
