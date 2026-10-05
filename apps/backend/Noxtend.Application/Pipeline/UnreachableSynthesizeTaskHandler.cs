namespace Noxtend.Application.Pipeline;

/// <summary>
/// `TaskKind.Synthesize` 는 절대 이 경로로 오면 안 된다 (spec 20260917).
///
/// 합성(대칭) 이미지 생성은 외부 API 호출이 없는 로컬 연산이라, 계획→Claim→반전
/// I/O→Succeed→이미지 부착까지 애플리케이션 핸들러 하나가 한 트랜잭션으로 끝낸다 —
/// `Pending` 상태로 저장되는 순간 자체가 없다. 이 핸들러는
/// `TaskWorkerRegistration`의 "모든 `TaskKind`는 워커가 있어야 한다"는 기계적 규칙만
/// 채우는 자리이고, 실제로 호출되면 그 자체가 버그다.
/// </summary>
public sealed class UnreachableSynthesizeTaskHandler : ITaskHandler
{
    public Task<RunTaskOutcome> HandleAsync(Guid taskId, CancellationToken ct)
        => throw new InvalidOperationException(
            $"Synthesize 공정이 큐에 도달했습니다({taskId}) — 항상 한 트랜잭션 안에서 즉시 끝나야 합니다.");
}
