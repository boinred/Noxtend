namespace Noxtend.Domain.Ports;

/// <summary>
/// 현재 시각.
///
/// Design Ref: §3.2 · §8.2 #25 — 리스 만료·타임스탬프가 전부 시각에 걸려 있다.
/// <c>DateTimeOffset.UtcNow</c> 를 직접 부르면 "리스가 만료된 공정" 같은 테스트가
/// 실제 시간을 기다려야 한다. 고정 시계를 넣을 수 있어야 결정적으로 검증된다.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
