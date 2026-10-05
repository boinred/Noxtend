using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Infrastructure.Persistence.Repositories;

namespace Noxtend.Tests.Infrastructure;

public sealed class GeneratedImageRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetsGeneratedImageById_WithoutItsOwner()
    {
        var options = new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new NoxtendDbContext(options);
        var repository = new EfJobRepository(db);

        // 생성 이미지가 포함된 작업 aggregate
        var job = PipelineJob.Create(
            AssetCategory.Background,
            Guid.NewGuid(),
            Now,
            Guid.NewGuid(),
            "gemini-image");
        job.ApplyParts(["석조 다리"]);
        var decompose = job.PlanTask(TaskKind.Decompose, ordinal: 0);
        decompose.Claim(Now, TimeSpan.FromMinutes(1));
        decompose.Succeed(Now);
        job.PlanReadyFollowUpTasks();

        var generate = Assert.Single(
            job.Tasks,
            task => task.Kind == TaskKind.Generate && task.ViewDirection == ViewDirection.Front);
        var part = Assert.Single(job.Parts);
        job.AttachGeneratedImage(
            part.Id,
            generate.Id,
            "generated/stone-bridge.png",
            "image/png",
            2048,
            Now);
        var expected = Assert.Single(job.GeneratedImages);

        await repository.AddAsync(job, CancellationToken.None);
        await repository.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var actual = await repository.GetGeneratedImageAsync(expected.Id, CancellationToken.None);

        Assert.NotNull(actual);
        Assert.Equal("generated/stone-bridge.png", actual.BlobKey);
        Assert.Equal("image/png", actual.ContentType);
    }
}
