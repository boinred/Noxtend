namespace Noxtend.Domain.Provider;

/// <summary>
/// AI 공급자 설정 — 관리자가 등록한 것 중에서 실행 시 고른다.
///
/// Design Ref: §2.2 키의 수명 · §7
///
/// **평문 키가 이 엔티티에 들어오지 않는다.** 암호문과 끝 4자리만 갖는다.
/// 복호화는 Infrastructure 의 Factory 안에서 일어나고 그 안에서 끝난다 —
/// Application·Domain 은 키를 본 적이 없다. 인증이 없는 사이클이므로
/// 노출 경로를 규율이 아니라 구조로 막는다.
///
/// **모델은 여기 없다.** 공정이 갖는다 (<see cref="Job.PipelineTask.Model"/>).
/// 여기에 두면 "공급자 하나 = 모델 하나" 가 되어 같은 키로 두 모델을 쓰려면
/// 공급자를 두 번 등록해야 한다. 이 엔티티는 **접속 수단**만 갖는다.
/// </summary>
public sealed class ProviderConfig
{
    private ProviderConfig()
    {
        // EF Core 재구성용
    }

    private ProviderConfig(
        Guid id,
        string displayName,
        ProviderKind kind,
        string apiKeyCipher,
        string apiKeyLast4,
        DateTimeOffset now)
    {
        Id = id;
        DisplayName = displayName;
        Kind = kind;
        ApiKeyCipher = apiKeyCipher;
        ApiKeyLast4 = apiKeyLast4;
        IsEnabled = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public ProviderKind Kind { get; private set; }

    /// <summary>절대 응답에 담기지 않는다. 응답 DTO 에는 <see cref="MaskedApiKey"/> 만 실린다.</summary>
    public string ApiKeyCipher { get; private set; } = string.Empty;

    public string ApiKeyLast4 { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>화면에 보이는 형태. 끝 4자리만 남는다 (§4.2 #9).</summary>
    public string MaskedApiKey => $"••••••••{ApiKeyLast4}";

    /// <summary>
    /// 암호문과 끝 4자리를 받는다. 평문은 호출자(Infrastructure)가 이미 처리했다 —
    /// 이 시그니처가 평문을 받지 않는 것이 계약이다.
    /// </summary>
    public static ProviderConfig Create(
        string displayName,
        ProviderKind kind,
        string apiKeyCipher,
        string apiKeyLast4,
        DateTimeOffset now)
        => new(Guid.NewGuid(), displayName, kind, apiKeyCipher, apiKeyLast4, now);

    /// <summary>
    /// 수정. <paramref name="apiKeyCipher"/> 가 <c>null</c> 이면 기존 키를 유지한다.
    ///
    /// 화면이 키를 되읽을 수 없으므로 수정마다 재입력을 요구하면 실수로 지워진다 (§4.2 #11).
    /// </summary>
    public void Update(
        string displayName,
        ProviderKind kind,
        string? apiKeyCipher,
        string? apiKeyLast4,
        bool isEnabled,
        DateTimeOffset now)
    {
        DisplayName = displayName;
        Kind = kind;
        IsEnabled = isEnabled;
        UpdatedAt = now;

        if (apiKeyCipher is not null && apiKeyLast4 is not null)
        {
            ApiKeyCipher = apiKeyCipher;
            ApiKeyLast4 = apiKeyLast4;
        }
    }

    /// <summary>끝 4자리 추출. 4자 미만이면 있는 만큼만 남긴다.</summary>
    public static string ExtractLast4(string plainApiKey)
        => plainApiKey.Length <= 4 ? plainApiKey : plainApiKey[^4..];
}
