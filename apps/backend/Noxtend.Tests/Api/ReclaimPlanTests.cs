using Noxtend.Api.Workers;
using Noxtend.Application.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Tests.Api;

/// <summary>
/// 스위퍼가 **어느 스트림을 얼마나 오래 방치된 것부터** 회수하는가.
///
/// Design Ref: §8.4 · Plan D-10
///
/// **`TaskWorkerRegistration.Stages` 와 같은 이유로 값으로 꺼낸다.** 스위퍼는 타이머를
/// 도는 백그라운드 서비스라 그 안에서 확인할 수 없는데, 빠뜨린 종류는 예외도 로그도
/// 없이 조용히 쌓이기만 한다. 등록에서 그랬듯 여기서도 개수로 고정한다.
///
/// 지금은 Extract 하나로 고정돼 있어 Generate 의 미확인 메시지가 영원히 남는다.
/// </summary>
public sealed class ReclaimPlanTests
{
    /// <summary>
    /// 종류를 열거형에 더하고 회수 계획에서 빠뜨리면 그 스트림의 미확인 메시지가
    /// 영원히 남는다 — 워커가 죽어도 아무도 되살리지 않는다.
    /// </summary>
    [Fact]
    public void EveryTaskKindGetsReclaimed()
    {
        var kinds = ReclaimPlan.For(new JobOptions(), new GenerationOptions())
            .Select(step => step.Kind)
            .OrderBy(kind => kind);

        Assert.Equal(Enum.GetValues<TaskKind>().OrderBy(kind => kind), kinds);
    }

    /// <summary>
    /// 방치 기준은 **그 단계의 리스보다 넉넉해야 한다.** 살아 있는 워커의 메시지를 뺏으면
    /// 같은 공정이 두 번 돌아 이미지가 두 번 만들어진다.
    ///
    /// 생성은 리스가 텍스트 단계의 세 배라 같은 기준을 쓰면 정상 작업을 회수하게 된다.
    /// </summary>
    [Fact]
    public void EachKindWaitsTwiceItsOwnLease()
    {
        var job = new JobOptions { LeaseSeconds = 100 };
        var generation = new GenerationOptions { LeaseSeconds = 400 };

        var plan = ReclaimPlan.For(job, generation).ToDictionary(s => s.Kind, s => s.IdleLongerThan);

        Assert.Equal(TimeSpan.FromSeconds(200), plan[TaskKind.Extract]);
        Assert.Equal(TimeSpan.FromSeconds(800), plan[TaskKind.Generate]);
        Assert.Equal(TimeSpan.FromSeconds(800), plan[TaskKind.GenerateSprite]);
    }
}
