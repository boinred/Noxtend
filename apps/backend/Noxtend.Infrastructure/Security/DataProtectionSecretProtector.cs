using Microsoft.AspNetCore.DataProtection;
using Noxtend.Domain.Ports;

namespace Noxtend.Infrastructure.Security;

/// <summary>
/// Design Ref: §2.2 · §7 — ASP.NET Core Data Protection.
///
/// 보호 키는 마운트 볼륨에 영속한다 (deploy/k8s/api.yaml). 파드가 재시작할 때 링이
/// 새로 만들어지면 저장된 공급자 키가 전부 복호화 불가가 된다.
///
/// Purpose 문자열이 격리 경계다. 다른 목적으로 만든 암호문은 여기서 풀리지 않는다.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private const string Purpose = "Noxtend.ProviderApiKey.v1";

    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string Protect(string plain) => _protector.Protect(plain);

    public string Unprotect(string cipher) => _protector.Unprotect(cipher);
}
