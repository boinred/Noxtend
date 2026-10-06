using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>목록 한 쪽과 그 조건의 전체 개수.</summary>
public sealed record JobListPage(IReadOnlyList<PipelineJob> Items, int Total);

/// <summary>
/// 작업 목록 — 홈 두 섹션. Design Ref: §4.2 #7
///
/// <c>active</c> 가 "실행 중", <c>terminal</c> 이 "최근 작업" 이다. 카테고리 필터는
/// 스튜디오 3종(캐릭터·소품·배경)을 대비한 것이며, 백본이 카테고리를 읽는 것이 아니라
/// 조회 조건일 뿐이다 (§2.4).
/// </summary>
public sealed class ListJobsHandler(IJobRepository jobs)
{
    /// <summary>홈이 요약만 필요하므로 상한을 둔다. 무제한 조회는 목록 화면의 요구가 아니다.</summary>
    private const int MaxLimit = 50;

    /// <summary>
    /// 목록과 **전체 개수**를 함께 낸다.
    ///
    /// 상한이 있으니 화면의 수와 실제 수가 다르다. 하나를 지우면 다음 것이 올라와
    /// 수가 그대로인데, 그것만 보면 삭제가 안 된 것처럼 읽힌다.
    /// </summary>
    public async Task<JobListPage> HandleAsync(
        JobListFilter filter,
        AssetCategory? category,
        int limit,
        CancellationToken ct,
        ProductionMode? productionMode = null)
    {
        var items = await jobs.ListAsync(filter, category, Math.Clamp(limit, 1, MaxLimit), ct, productionMode);
        var total = await jobs.CountAsync(filter, category, ct, productionMode);

        return new JobListPage(items, total);
    }
}
