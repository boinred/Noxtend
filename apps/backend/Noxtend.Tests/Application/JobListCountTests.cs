using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Tests.Application;

/// <summary>
/// 목록이 몇 건인지 세는 일.
///
/// **상한과 무관해야 한다.** 홈은 열 건만 싣는데 그 길이를 전체로 쓰면, 열한 건일 때
/// 하나를 지워도 열한 번째가 올라와 수가 그대로 열이다 — 사용자는 삭제가 안 된 줄로 읽는다.
/// </summary>
public sealed class JobListCountTests
{
    [Fact]
    public async Task Count_IgnoresTheListLimit()
    {
        var fixture = new PipelineFixture();

        // 상한(10)을 넘기는 종료 작업
        for (var i = 0; i < 12; i++)
        {
            var job = await fixture.StartJobAsync();
            await fixture.RunAllStagesAsync(job);
        }

        var listed = await fixture.Jobs.ListAsync(
            JobListFilter.Terminal, null, limit: 10, CancellationToken.None);
        var total = await fixture.Jobs.CountAsync(JobListFilter.Terminal, null, CancellationToken.None);

        Assert.Equal(10, listed.Count);
        Assert.Equal(12, total);
    }

    /// <summary>진행 중과 끝난 것을 섞어 세면 두 섹션의 수가 서로의 것을 포함한다.</summary>
    [Fact]
    public async Task Count_SeparatesActiveFromTerminal()
    {
        var fixture = new PipelineFixture();

        var done = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(done);
        await fixture.StartJobAsync();

        Assert.Equal(1, await fixture.Jobs.CountAsync(JobListFilter.Terminal, null, CancellationToken.None));
        Assert.Equal(1, await fixture.Jobs.CountAsync(JobListFilter.Active, null, CancellationToken.None));
    }

    /// <summary>카테고리를 거는 화면이 생기면 개수도 같은 조건이어야 한다.</summary>
    [Fact]
    public async Task Count_RespectsTheCategoryFilter()
    {
        var fixture = new PipelineFixture();

        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);

        Assert.Equal(1, await fixture.Jobs.CountAsync(
            JobListFilter.Terminal, AssetCategory.Background, CancellationToken.None));
        Assert.Equal(0, await fixture.Jobs.CountAsync(
            JobListFilter.Terminal, AssetCategory.Character, CancellationToken.None));
    }
}
