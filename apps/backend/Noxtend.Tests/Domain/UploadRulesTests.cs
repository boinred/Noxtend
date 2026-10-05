using Noxtend.Domain.Validation;

namespace Noxtend.Tests.Domain;

/// <summary>
/// Design Ref: §8.2 #10 — 업로드 검증. **서버가 정본이다** (§3.4).
/// 프론트의 같은 규칙(L1-F #1·#2)과 이 테스트가 짝을 이룬다.
/// </summary>
public sealed class UploadRulesTests
{
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("IMAGE/PNG")]
    public void Validate_AcceptsAllowedTypes(string contentType)
        => Assert.Null(UploadRules.Validate(contentType, 1024));

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData(null)]
    public void Validate_RejectsTypesOutsideWhitelist(string? contentType)
        => Assert.Equal(UploadRejection.UnsupportedType, UploadRules.Validate(contentType, 1024));

    [Fact]
    public void Validate_RejectsEmptyFile()
        => Assert.Equal(UploadRejection.Empty, UploadRules.Validate("image/png", 0));

    [Fact]
    public void Validate_RejectsFileOverLimit()
        => Assert.Equal(
            UploadRejection.TooLarge,
            UploadRules.Validate("image/png", UploadRules.MaxImageBytes + 1));

    [Fact]
    public void Validate_AcceptsFileExactlyAtLimit()
        => Assert.Null(UploadRules.Validate("image/png", UploadRules.MaxImageBytes));

    [Fact]
    public void Validate_ReportsEmptyBeforeType()
        // 형식도 크기도 틀렸을 때 "빈 파일" 이 먼저다 — 내용이 없으면 형식은 부차적이다
        => Assert.Equal(UploadRejection.Empty, UploadRules.Validate("application/pdf", 0));
}
