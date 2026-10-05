using Noxtend.Domain.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Tests.Application;

/// <summary>
/// 끝난 작업 삭제.
///
/// **판정과 삭제가 따로 돌면 안 된다.** 읽어서 확인하고 따로 지우면 그 사이에 재시도나
/// 3D 추가가 작업을 다시 열 수 있고, 그때 삭제가 진행되면 유료 외부 작업이 돌고 있는데
/// 그것을 추적할 기록이 사라진다.
/// </summary>
public sealed class DeleteJobHandlerTests
{
    [Fact]
    public async Task Delete_TerminalJob_SucceedsAndRemovesFromRepository()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        // 종료 상태 작업 삭제 요청
        var result = await fixture.Delete.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await fixture.Jobs.GetAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_ActiveJob_FailsWithJobActiveCannotDelete()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        // 진행 중 작업 삭제 차단 검증
        var result = await fixture.Delete.HandleAsync(job.Id, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobActiveCannotDelete, result.ErrorCode);
        Assert.NotNull(await fixture.Jobs.GetAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_UnknownJob_FailsWithJobNotFound()
    {
        var fixture = new PipelineFixture();

        // 존재하지 않는 작업 ID 삭제 요청
        var result = await fixture.Delete.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.JobNotFound, result.ErrorCode);
    }

    /// <summary>
    /// **파일까지 지운다.**
    ///
    /// 목록에서만 치우면 저장소에는 이미지와 3D 가 그대로 남아 용량을 먹는다.
    /// 사용자가 "지웠다" 고 믿는 것과 실제가 달라진다.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheStoredFilesToo()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        var before = fixture.Blobs.Count;
        Assert.True(before > 0, "지울 파일이 있어야 검사가 성립한다");

        await fixture.Delete.HandleAsync(job.Id, CancellationToken.None);

        Assert.Equal(0, fixture.Blobs.Count);
    }

    /// <summary>
    /// **파일 삭제가 실패해도 삭제는 성공이다.**
    ///
    /// 기록이 없어진 뒤라 되돌릴 것이 없다. 여기서 오류를 올리면 사용자는 이미 지워진
    /// 작업을 두고 "삭제 실패" 를 보게 되고, 다시 눌러도 404 만 받는다.
    /// </summary>
    [Fact]
    public async Task Delete_SucceedsEvenWhenBlobRemovalFails()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        fixture.Blobs.FailDeletes = true;

        var result = await fixture.Delete.HandleAsync(job.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(await fixture.Jobs.GetAsync(job.Id, CancellationToken.None));
    }

    /// <summary>
    /// **원본은 공유될 수 있다.** 같은 업로드로 두 번 접수하면 한쪽을 지워도 원본은
    /// 남아야 한다 — 확인 없이 지우면 남은 작업의 원본이 사라진다.
    /// </summary>
    [Fact]
    public async Task Delete_KeepsTheSourceImage_WhenAnotherJobStillUsesIt()
    {
        var fixture = new PipelineFixture();
        var first = await fixture.StartJobAsync();
        var second = await fixture.StartJobAsync(first.SourceImageId);

        await fixture.RunAllStagesAsync(first);

        var blobs = await fixture.Jobs.DeleteIfTerminalAsync(first.Id, CancellationToken.None);

        Assert.NotNull(blobs);
        Assert.DoesNotContain(blobs!.Images, key => key.Contains(second.SourceImageId.ToString("n")));
    }

    /// <summary>
    /// **종료 상태 목록이 곧 삭제 허용 목록이다** (리뷰 P2).
    ///
    /// 문서에는 성공·실패·취소 셋만 적혀 있었는데 실제로는 부분 성공도 포함이다.
    /// 이미지 일부만 나온 작업도 더 진행하지 않으므로 종료가 맞다 — 목록을 값으로
    /// 두고 여기서 못박아, 상태가 늘 때 조용히 어긋나지 않게 한다.
    /// </summary>
    [Fact]
    public void TerminalStatuses_IncludePartialSuccess()
    {
        Assert.Equal(
            [
                JobStatus.Succeeded,
                JobStatus.PartiallySucceeded,
                JobStatus.Failed,
                JobStatus.Canceled,
            ],
            PipelineJob.TerminalStatuses);
    }

    /// <summary>
    /// **저장소가 상태를 다시 본다** (리뷰 P1).
    ///
    /// 판정과 삭제가 따로 돌면 그 사이에 재시도나 3D 추가가 작업을 다시 열 수 있고,
    /// 그때 삭제가 진행되면 유료 외부 작업이 돌고 있는데 추적할 기록이 사라진다.
    /// 호출자가 무엇을 들고 있든 저장소가 지금 상태로 판정한다.
    /// </summary>
    [Fact]
    public async Task DeleteIfTerminal_RefusesRunningJob_AndKeepsIt()
    {
        var fixture = new PipelineFixture();
        var job = await fixture.StartJobAsync();

        Assert.False(job.IsTerminal);

        var removed = await fixture.Jobs.DeleteIfTerminalAsync(job.Id, CancellationToken.None);

        Assert.Null(removed);
        Assert.NotNull(await fixture.Jobs.GetAsync(job.Id, CancellationToken.None));
    }
}
