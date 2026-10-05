namespace Noxtend.Domain.Validation;

/// <summary>
/// 업로드 검증 — 형식 화이트리스트와 크기 상한.
///
/// Design Ref: §3.4 · §7 — 같은 규칙이 프론트에도 있지만 **서버가 정본**이다.
/// 클라이언트 검증은 즉시 피드백용이고, 클라이언트를 신뢰하지 않는다 (§2.2).
///
/// 블랙리스트가 아니라 화이트리스트인 이유는 새 형식이 생겨도 기본값이 거부이기 때문이다.
/// </summary>
public static class UploadRules
{
    public static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
        };

    public const long MaxImageBytes = 12L * 1024 * 1024;

    /// <summary>위반 사유. 위반이 없으면 <c>null</c>.</summary>
    public static UploadRejection? Validate(string? contentType, long sizeBytes)
    {
        // 빈 파일을 먼저 본다. 형식이 맞아도 내용이 없으면 공급자에게 보낼 것이 없다
        if (sizeBytes <= 0)
        {
            return UploadRejection.Empty;
        }

        if (contentType is null || !AllowedContentTypes.Contains(contentType))
        {
            return UploadRejection.UnsupportedType;
        }

        return sizeBytes > MaxImageBytes ? UploadRejection.TooLarge : null;
    }
}

/// <summary>Design Ref: §4.2 #2 — 오류 코드 UPLOAD_EMPTY · UPLOAD_UNSUPPORTED_TYPE · UPLOAD_TOO_LARGE 로 매핑된다.</summary>
public enum UploadRejection
{
    Empty,
    UnsupportedType,
    TooLarge,
}
