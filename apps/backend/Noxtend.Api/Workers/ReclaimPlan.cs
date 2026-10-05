using Noxtend.Application.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Api.Workers;

/// <summary>
/// 스위퍼가 회수할 스트림과 그 방치 기준.
///
/// Design Ref: §8.4 · Plan D-10
///
/// **종류가 하나뿐이던 시절에는 필요 없던 값이다.** 스위퍼가 Extract 스트림만 회수하고
/// 있었고, 그 사이 Generate 의 미확인 메시지는 아무도 되살리지 않았다. 종류를 늘리면서
/// 같은 실수를 반복하지 않도록 <see cref="TaskWorkerRegistration.Stages"/> 처럼 값으로
/// 꺼내 테스트가 열거형과 대조하게 한다.
/// </summary>
public static class ReclaimPlan
{
    /// <summary>
    /// **기준은 리스의 두 배다.** 살아 있는 워커가 물고 있는 메시지를 뺏으면 같은 공정이
    /// 두 번 돌고, 생성 공정이라면 이미지가 두 번 만들어져 실제로 돈이 나간다.
    ///
    /// 단계마다 리스가 다르므로 기준도 단계마다 다르다 — 생성은 텍스트 단계의 세 배다.
    /// </summary>
    public static IReadOnlyList<ReclaimStep> For(
        JobOptions job, GenerationOptions generation, MeshGenerationOptions? mesh = null)
        => [.. Enum.GetValues<TaskKind>()
            .Select(kind => new ReclaimStep(
                kind, Idle(kind, job, generation, mesh ?? new MeshGenerationOptions())))];

    private static TimeSpan Idle(
        TaskKind kind, JobOptions job, GenerationOptions generation, MeshGenerationOptions mesh)
        => kind switch
        {
            TaskKind.Generate => generation.Lease * 2,

            // 3D 는 외부 작업이 10~120초 걸려 리스가 분 단위다. 텍스트 기준을 쓰면
            // 살아 있는 워커의 메시지를 뺏는다
            TaskKind.Reconstruct => mesh.Lease * 2,

            _ => job.Lease * 2,
        };
}

public sealed record ReclaimStep(TaskKind Kind, TimeSpan IdleLongerThan);
