namespace Noxtend.Domain.Ports;

/// <summary>
/// 비밀 암복호화.
///
/// Design Ref: §2.2 · §7 — 구현은 ASP.NET Core Data Protection 이고 보호 키는
/// 마운트 볼륨에 영속한다. 이 Port 를 쓰는 곳은 Infrastructure 뿐이다 —
/// Application 이 이것을 부르면 평문이 계층을 건너오게 된다.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plain);

    string Unprotect(string cipher);
}
