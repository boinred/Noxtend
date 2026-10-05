using Noxtend.Application.Job;

namespace Noxtend.Tests.Application;

/// <summary>
/// character-studio §4.3 — 힌트 코덱의 왕복 계약.
///
/// 접수가 직렬화하고 스테이지가 역직렬화한다(slice 3). 그 사이에 값이 조용히 사라지면
/// 힌트가 프롬프트에 안 실려 생성에서야 이상해진다 — 코덱 단위에서 왕복을 고정한다.
/// </summary>
public sealed class PartHintCodecTests
{
    // 직렬화 → 역직렬화 왕복이 값을 보존한다
    [Fact]
    public void RoundTrip_PreservesValues()
    {
        IReadOnlyList<PartHint> hints = [new PartHint("팔찌", 3, null), new PartHint("장갑", 2, "손목형")];

        var restored = PartHintCodec.Deserialize(PartHintCodec.Serialize(hints));

        Assert.Equal(hints, restored);
    }

    // 빈 목록·null 은 저장하지 않는다(null), 그리고 null 역직렬화는 빈 목록이다
    [Fact]
    public void EmptyAndNull_AreHandled()
    {
        Assert.Null(PartHintCodec.Serialize(null));
        Assert.Null(PartHintCodec.Serialize([]));
        Assert.Empty(PartHintCodec.Deserialize(null));
        Assert.Empty(PartHintCodec.Deserialize(""));
    }

    // 외부에서 온 camelCase JSON 도 값이 살아야 한다 — 대소문자 민감이면 조용히 빈 값이 된다
    [Fact]
    public void Deserialize_IsCaseInsensitive()
    {
        var restored = PartHintCodec.Deserialize("""[{"type":"팔찌","count":3,"variant":"두께형"}]""");

        var one = Assert.Single(restored);
        Assert.Equal("팔찌", one.Type);
        Assert.Equal(3, one.Count);
        Assert.Equal("두께형", one.Variant);
    }

    // 줄글 렌더(slice 3) — 변형 있으면 "종류(변형) 개수", 없으면 "종류 개수", 항목은 ", " 로 잇는다
    [Fact]
    public void Render_FormatsHumanReadableLine()
    {
        var json = PartHintCodec.Serialize([new PartHint("팔찌", 3, null), new PartHint("장갑", 2, "손목형")]);

        Assert.Equal("팔찌 3, 장갑(손목형) 2", PartHintCodec.Render(json));
    }

    // 빈 목록·null 은 빈 문자열이 아니라 "없음" 폴백(§4.2) — Render 예외를 피한다
    [Fact]
    public void Render_EmptyOrNull_FallsBackToAbsent()
    {
        Assert.Equal("없음", PartHintCodec.Render(null));
        Assert.Equal("없음", PartHintCodec.Render(""));
        Assert.Equal("없음", PartHintCodec.Render(PartHintCodec.Serialize([])));
    }
}
