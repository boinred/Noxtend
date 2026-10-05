namespace Noxtend.Domain.Ports;

/// <summary>
/// 설정 id 로 공급자 어댑터를 만든다.
///
/// Design Ref: §2.2 키의 수명 · §3.2
///
/// **이 시그니처가 보안 경계다.** 설정 조회·복호화·어댑터 생성이 Infrastructure 안에서
/// 끝나므로 Application 은 id 만 넘기고 키를 본 적이 없다. 만약 Application 이
/// <c>ProviderConfig</c> 를 읽어 키를 풀어 넘기는 형태였다면 평문이 계층을 건너온다.
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// 모델은 공급자 설정이 아니라 호출자가 정한다 — 공정이 모델을 소유하므로
    /// (<see cref="Job.PipelineTask.Model"/>) 같은 키로 여러 모델을 부를 수 있다.
    /// </summary>
    Task<ILlmProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct);
}
