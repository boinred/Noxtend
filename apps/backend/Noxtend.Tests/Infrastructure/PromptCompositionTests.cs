using System.Security.Cryptography;
using System.Text;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 프롬프트 조합 불변식. Design Ref: download-view-consistency §1.1 · §3.3
///
/// **캐릭터 문면은 1바이트도 변하면 안 된다.** 회전 계약을 공용 조각으로 빼는
/// 리팩터가 캐릭터 시드를 바꾸면 캐릭터 마이그레이션이 필요해지고 무회귀(NFR-02)가
/// 깨진다. 해시는 리팩터 **전** 문면에서 채취했다 — 조합이 그 문면을 재현해야 한다.
/// </summary>
public sealed class PromptCompositionTests
{
    // B-01 — 리팩터 전 GenerateCharacterSystem 의 SHA-256.
    // workstream K (2026-08-28)에서 벨트 제외 문구를 의도적으로 추가하며 해시를 갱신했다 —
    // 이 테스트는 "기계적 리팩터가 문면을 안 바꿨는지"를 지키는 것이지, 의도적 내용 변경까지
    // 막는 것은 아니다.
    // 2026-09-03 모델러 피드백으로 두 조항을 의도적으로 더하며 해시를 갱신했다 —
    // 손목 아래 장갑의 편 손가락 자세(#13), 관절을 감싸는 갑주 캡 형태(#11).
    // 2026-09-08 armor-any-region 로 세 번째 갱신 — 관절 캡 조건을 임의 관절로 일반화하고,
    // 서술에 없는 겹침 표시를 그리지 않는 규칙(②-3)을 추가했다.
    // 2026-09-08 리뷰 결함 수정으로 네 번째 갱신 — Tail 의 "Do not reproduce other parts" 에
    // ②-3 문단을 가리키는 상호 참조를 붙였다(뜻은 안 바뀐다).
    [Fact]
    public void CharacterGenerateSystem_IsByteIdenticalAfterTheRefactor()
    {
        var actual = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(SeedPrompts.CharacterGenerate().System)));

        Assert.Equal("6C8B6190BCB1C0B7EF4AAABFB2FFFA6528A27422D89D6BF684EF47585C3EEA47", actual);
    }

    /// <summary>B-06 — 배경 v2 에 회전 계약이 실제로 들어 있고, 어휘는 배경의 것이다.</summary>
    [Fact]
    public void BackgroundGenerateV2_CarriesTheRotationContract_WithSceneWording()
    {
        var (system, _, _, _) = SeedPrompts.BackgroundGenerateV2();

        // 계약의 핵심 문장들 — 이게 없으면 v2 를 심을 이유가 없다
        Assert.Contains("reference image 1 is the FRONT view", system);
        Assert.Contains("SAME physical object", system);
        Assert.Contains("One-sided surfaces", system);

        // 배경 어휘 — 캐릭터 전용 어휘가 새면 모델이 전신 사진을 찾는다
        Assert.Contains("the original scene photo", system);
        Assert.DoesNotContain("full-body", system);
        Assert.DoesNotContain("gender", system);

        // 배경 전용 조항
        Assert.Contains("ground-contact silhouette", system);
    }

    /// <summary>토큰이 안 치환된 채 새면 모델이 {{original}} 을 읽는다.</summary>
    [Fact]
    public void ComposedPrompts_LeaveNoUnreplacedTokens()
    {
        Assert.DoesNotContain("{{original}}", SeedPrompts.BackgroundGenerateV2().System);
        Assert.DoesNotContain("{{original}}", SeedPrompts.CharacterGenerate().System);
    }
}
