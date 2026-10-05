using Noxtend.Infrastructure.Persistence.Serialization;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 색상어 사전이 **실제 레거시 데이터**를 얼마나 읽어내는가 (Design §4.4 · FR-11).
///
/// **여기 값은 지어낸 것이 아니다.** 개발 DB 의 구형 `SceneJson` 145개 값에서 뽑았다.
/// 설계가 든 예시 표만 옮겼을 때는 32%가 미해결로 남았는데, 그 미해결 목록이 곧 이
/// 테스트의 입력이다 — 흔한 색상어조차 없어 화면의 3분의 1이 미확정 칩이 됐다.
///
/// FR-12 가 미확정을 허용하므로 "모르는 것은 null" 규칙은 그대로다. 다만 **모르는 범위가
/// 실제 데이터에 비해 지나치게 넓지 않아야** 호환 표시가 의미를 갖는다.
/// </summary>
public sealed class LegacyPaletteColorResolverTests
{
    /// <summary>실제 DB 에서 미해결로 남던 표현들. 전부 색을 가져야 한다.</summary>
    [Theory]
    [InlineData("선명한 청록색 물")]
    [InlineData("짙은 청록 그림자")]
    [InlineData("어두운 청색 금속색")]
    [InlineData("짙은 남청색")]
    [InlineData("짙은 청람색 암벽과 그림자")]
    [InlineData("소량의 밝은 파란 발광")]
    [InlineData("연한 하늘색")]
    [InlineData("밝은 터키석색")]
    [InlineData("시안")]
    [InlineData("이끼빛 녹색")]
    [InlineData("짙은 숲 녹색")]
    [InlineData("밝은 황록색 잎")]
    [InlineData("올리브색 식생")]
    [InlineData("모스 그린")]
    [InlineData("따뜻한 베이지")]
    [InlineData("크림색 돌바닥")]
    [InlineData("연한 모래색 포석")]
    [InlineData("저채도 황토색")]
    [InlineData("따뜻한 황금색")]
    [InlineData("소량의 따뜻한 노란 꽃색")]
    [InlineData("회갈색 석재")]
    [InlineData("짙은 차콜 지붕")]
    public void RealLegacyDescriptions_ResolveToAColour(string description)
    {
        Assert.NotNull(LegacyPaletteColorResolver.Resolve(description));
    }

    /// <summary>
    /// 구체적인 구문이 이긴다.
    ///
    /// `회청색` 을 `청색` 으로 읽으면 회색기가 사라지고, `남청색` 을 `청색` 으로 읽으면
    /// 어둠이 사라진다. 부분 문자열이 이기면 구체적일수록 더 틀린 색이 된다.
    /// </summary>
    [Fact]
    public void MoreSpecificPhrasesWin()
    {
        var plainBlue = LegacyPaletteColorResolver.Resolve("어두운 청색 금속색");

        Assert.NotEqual(plainBlue, LegacyPaletteColorResolver.Resolve("회청색 석재"));
        Assert.NotEqual(plainBlue, LegacyPaletteColorResolver.Resolve("짙은 남청색"));
        Assert.NotEqual(plainBlue, LegacyPaletteColorResolver.Resolve("짙은 청람색 암벽"));
    }

    /// <summary>
    /// 그래도 모르는 것은 모른다고 한다 (FR-12).
    ///
    /// 사전을 넓히는 것과 아무 색이나 채우는 것은 다르다. 틀린 색이 사실처럼 보이는 쪽이
    /// 빈 칩보다 나쁘다.
    /// </summary>
    [Theory]
    [InlineData("젖은 포장 표면")]
    [InlineData("거친 질감의 벽")]
    public void UnknownDescriptionsStayNull(string description)
    {
        Assert.Null(LegacyPaletteColorResolver.Resolve(description));
    }
}
