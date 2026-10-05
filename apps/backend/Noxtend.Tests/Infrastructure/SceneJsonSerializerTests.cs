using Noxtend.Domain.Job;
using Noxtend.Infrastructure.Persistence.Serialization;
using Noxtend.Tests.Domain;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 저장된 장면을 읽는 경계 (Design §6 · D-05).
///
/// **DB 에는 팔레트가 문자열 배열인 행이 이미 있다.** 그것들을 SQL 로 한 번에 바꾸지 않고
/// 읽을 때 승격한다 — 자연어 색상어 해석은 SQL 로 표현할 수 없고, 일괄 UPDATE 는 롤백이
/// 위험하기 때문이다.
///
/// 승격은 **결정적**이어야 한다. 조회 중에 LLM 이나 이미지 API 를 부르면 같은 행이 볼
/// 때마다 다른 색으로 보인다.
/// </summary>
public sealed class SceneJsonSerializerTests
{
    /// <summary>구형 저장 형식 — 팔레트만 문자열 배열이고 나머지는 지금과 같다.</summary>
    private static string LegacyJson(params string[] palette)
    {
        var items = string.Join(",", palette.Select(p => $"\"{p}\""));

        return $$"""
            {
              "Palette": [{{items}}],
              "TimeOfDay": "해질녘",
              "Mood": "고요",
              "RenderingStyle": "수채",
              "MaterialFeel": "목재",
              "Camera": { "Type": "one-point", "EyeLevel": "1.6m", "HorizonY": 0.55 },
              "Light": { "Direction": "좌측 후방", "Temperature": "따뜻함", "ShadowHardness": "부드러움" },
              "Scale": { "Object": "기둥", "RealWorldSize": "3m" }
            }
            """;
    }

    private static PaletteEntry FirstEntryOf(string legacyValue)
        => SceneJsonSerializer.Deserialize(LegacyJson(legacyValue))!.Palette[0];

    /// <summary>B-07 — 문자열 안에 HEX 가 박혀 있으면 그것이 가장 정확한 근거다.</summary>
    [Fact]
    public void LegacyString_WithEmbeddedHex_SplitsNameAndColor()
    {
        Assert.Equal(new PaletteEntry("안개 회색", "#8B8980"), FirstEntryOf("안개 회색 #8B8980"));
    }

    /// <summary>HEX 만 있던 값은 이름도 그 HEX 다 — 지어낼 이름이 없다.</summary>
    [Fact]
    public void LegacyString_HexOnly_KeepsHexAsName()
    {
        Assert.Equal(new PaletteEntry("#2E5C6E", "#2E5C6E"), FirstEntryOf("#2E5C6E"));
    }

    /// <summary>B-08 — 색상어를 알아보면 대표 HEX 로 올린다. 이름은 그대로 둔다.</summary>
    [Fact]
    public void LegacyString_WithKnownColorWord_ResolvesToRepresentativeHex()
    {
        Assert.Equal(new PaletteEntry("백색 건물 외벽", "#FFFFFF"), FirstEntryOf("백색 건물 외벽"));
    }

    /// <summary>
    /// B-08a — 긴 구문이 먼저다.
    ///
    /// `청회색` 을 `회색` 으로 읽으면 푸른 기가 사라진다. 부분 문자열이 이기면 구체적인
    /// 표현일수록 더 틀린 색이 된다.
    /// </summary>
    [Fact]
    public void LegacyString_PrefersTheMoreSpecificPhrase()
    {
        Assert.Equal("#6B7C85", FirstEntryOf("청회색 지붕").Hex);
        Assert.Equal("#808080", FirstEntryOf("회색 지붕").Hex);
    }

    /// <summary>
    /// B-08b — 모르는 표현에 아무 색이나 넣지 않는다.
    ///
    /// 기본색을 채우면 틀린 색이 사실처럼 보인다. 이름은 지키고 색만 비운다.
    /// </summary>
    [Fact]
    public void LegacyString_WithoutAnyColorWord_KeepsNameAndLeavesHexNull()
    {
        Assert.Equal(new PaletteEntry("젖은 포장 표면", null), FirstEntryOf("젖은 포장 표면"));
    }

    /// <summary>
    /// 레거시에는 3~8개 규칙을 적용하지 않는다 (§4.3).
    ///
    /// 신규 응답 품질 규칙을 과거 데이터에 소급하면 장면이 통째로 사라진다.
    /// </summary>
    [Fact]
    public void LegacyPalette_IsNotHeldToTheNewCountRule()
    {
        var scene = SceneJsonSerializer.Deserialize(LegacyJson("백색 벽"));

        Assert.Single(scene!.Palette);
        Assert.Null(scene.Scale.HeightMeters);
    }

    /// <summary>빈 문자열은 이름도 색도 없다 — 칸만 차지한다.</summary>
    [Fact]
    public void LegacyPalette_DropsEmptyStrings()
    {
        var scene = SceneJsonSerializer.Deserialize(LegacyJson("백색 벽", "", "   "));

        Assert.Single(scene!.Palette);
    }

    /// <summary>
    /// B-09 — 신규 형식은 그대로 왕복한다.
    ///
    /// `SceneSpec` 통째로 비교하지 않는 이유는 레코드 동등성이 `IReadOnlyList` 를 참조로
    /// 보기 때문이다 — 내용이 같아도 다른 인스턴스면 다르다고 나온다.
    /// </summary>
    [Fact]
    public void StructuredPalette_RoundTrips()
    {
        var restored = SceneJsonSerializer.Deserialize(SceneJsonSerializer.Serialize(TestScene.Default));

        Assert.Equal(TestScene.Default.Palette, restored!.Palette);
        Assert.Equal(TestScene.Default with { Palette = [] }, restored with { Palette = [] });
    }

    /// <summary>
    /// B-10 — 깨진 JSON 한 행 때문에 목록 전체가 500 이 되면 안 된다.
    ///
    /// 값 변환기에서 예외가 나면 그 행을 건드리는 모든 조회가 터진다.
    /// </summary>
    [Theory]
    [InlineData("이건 JSON 이 아니다")]
    [InlineData("")]
    [InlineData(null)]
    public void BrokenJson_ReadsAsNoScene(string? json)
    {
        Assert.Null(SceneJsonSerializer.Deserialize(json));
    }
}
