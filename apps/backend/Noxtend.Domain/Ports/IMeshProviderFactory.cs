namespace Noxtend.Domain.Ports;

/// <summary>
/// 설정 id 로 3D 공급자를 만든다.
///
/// Design Ref: §6.2 · §13.1
///
/// <see cref="ILlmProviderFactory"/> 와 같은 이유로 id 만 받는다 — 키 조회·복호화가
/// Infrastructure 안에서 끝나야 평문이 계층을 건너오지 않는다.
/// </summary>
public interface IMeshProviderFactory
{
    Task<IMeshProvider> CreateAsync(Guid providerConfigId, string model, CancellationToken ct);
}
