using System.Text.Json;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Infrastructure;

public sealed class SeedPromptsTests
{
    [Fact]
    public void DecomposePromptUsesStablePartReferences()
    {
        // 모델 계약 — 자연어 이름은 설명이고 Pxx 만 파츠 관계의 식별자
        var (system, _, schema, _) = SeedPrompts.DecomposeV2();
        using var document = JsonDocument.Parse(schema);
        var itemProperties = document.RootElement
            .GetProperty("properties")
            .GetProperty("parts")
            .GetProperty("items")
            .GetProperty("properties");

        Assert.Contains("partRef", system, StringComparison.Ordinal);
        Assert.Contains("occludedBy", system, StringComparison.Ordinal);
        Assert.Contains("P01", system, StringComparison.Ordinal);
        Assert.True(itemProperties.TryGetProperty("partRef", out _));
        Assert.False(itemProperties.TryGetProperty("name", out _));
    }

    /// <summary>
    /// Analyze v2 는 팔레트를 이름과 색으로 나눠 요구한다 (Design §5.2).
    ///
    /// **스키마가 강제하지 않으면 모델은 자연어로 돌아간다.** v1 은 `items: string` 이라
    /// `"백색 건물 외벽"` 이 계약상 정상이었고, 그래서 화면에 색이 사라졌다.
    /// </summary>
    [Fact]
    public void AnalyzeV2SchemaRequiresNameAndHex()
    {
        var (system, _, schema, _) = SeedPrompts.AnalyzeV2();
        using var document = JsonDocument.Parse(schema);
        var palette = document.RootElement
            .GetProperty("properties")
            .GetProperty("palette");
        var items = palette.GetProperty("items");

        Assert.Equal("object", items.GetProperty("type").GetString());
        Assert.Equal(
            ["name", "hex"],
            items.GetProperty("required").EnumerateArray().Select(v => v.GetString()));

        // 짧은 표기나 알파 채널이 들어오면 CSS 와 프롬프트 양쪽이 흔들린다 (D-03)
        Assert.Equal(
            "^#[0-9A-Fa-f]{6}$",
            items.GetProperty("properties").GetProperty("hex").GetProperty("pattern").GetString());
        Assert.False(items.GetProperty("additionalProperties").GetBoolean());

        // 개수 범위가 없으면 한 칸짜리 팔레트가 계약상 정상이 된다 (D-04)
        Assert.Equal(3, palette.GetProperty("minItems").GetInt32());
        Assert.Equal(8, palette.GetProperty("maxItems").GetInt32());

        // 프롬프트도 같은 말을 해야 한다 — 스키마만 바꾸면 모델이 형식만 맞추고 색을 지어낸다
        Assert.Contains("hex", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>v1 은 과거 호출 재현용으로 손대지 않는다 (NFR-04 · D-12).</summary>
    [Fact]
    public void AnalyzeV1KeepsTheOldStringPalette()
    {
        var (_, _, schema, _) = SeedPrompts.For(TaskKind.Analyze);
        using var document = JsonDocument.Parse(schema);

        Assert.Equal(
            "string",
            document.RootElement
                .GetProperty("properties").GetProperty("palette")
                .GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void GeneratePromptFixesShadowAndBackgroundRules()
    {
        // 생성 프롬프트의 합성 가능 배경 계약
        var (system, _, _, _) = SeedPrompts.For(TaskKind.Generate);

        Assert.Contains("cast shadow", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contact shadow", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("form shading", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every sibling image", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#F2F2F2", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{viewDirection}}", system, StringComparison.Ordinal);
    }

    // ─────────────────────────── 캐릭터 시드 (slice 4 · §D-04) ───────────────────────────

    /// <summary>
    /// 캐릭터 추출은 분리 원칙과 힌트 범위 제한(소프트)을 문장으로 담고, 힌트 종류·성별을
    /// 변수로 받는다 (§D-04 · §D-03-1). 스키마는 공유 Interpret 가 읽는 parts 문자열 배열이라
    /// 기본과 같은 모양이어야 한다.
    /// </summary>
    [Fact]
    public void CharacterExtract_CarriesSeparationAndHintScope()
    {
        var (system, _, schema, _) = SeedPrompts.CharacterExtract();

        // 힌트 종류·성별을 존재 근거로 받는다
        Assert.Contains("{{partHints}}", system, StringComparison.Ordinal);
        Assert.Contains("{{gender}}", system, StringComparison.Ordinal);
        Assert.Contains("{{scene}}", system, StringComparison.Ordinal);

        // 분리 원칙 — 복합 파츠는 분리하되 표면 특징·머리는 규칙이 다르다
        Assert.Contains("separate", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("surface", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("head", system, StringComparison.OrdinalIgnoreCase);

        // 스키마 모양은 기본 추출과 같다 — 공유 Interpret 가 parts 문자열 배열을 읽는다
        using var document = JsonDocument.Parse(schema);
        Assert.Equal(
            "string",
            document.RootElement.GetProperty("properties").GetProperty("parts")
                .GetProperty("items").GetProperty("type").GetString());
    }

    /// <summary>
    /// 캐릭터 분해는 힌트 개수를 placements 기대치로, 성별을 베이스바디 구성으로 반영한다.
    /// 공유 Interpret 가 partRef·placements 를 읽으므로 스키마는 v3 모양이어야 한다.
    /// </summary>
    [Fact]
    public void CharacterDecompose_CarriesGenderAndPlacementSchema()
    {
        var (system, _, schema, _) = SeedPrompts.CharacterDecompose();

        Assert.Contains("{{gender}}", system, StringComparison.Ordinal);
        Assert.Contains("{{partHints}}", system, StringComparison.Ordinal);
        Assert.Contains("{{parts}}", system, StringComparison.Ordinal);

        // 성별은 몸통뿐 아니라 몸에 걸치는 파츠 실루엣에 반영된다 (사용자 지적, 재정의)
        Assert.Contains("silhouette", system, StringComparison.OrdinalIgnoreCase);

        // 공유 Interpret 계약 — partRef·placements 를 읽는다
        using var document = JsonDocument.Parse(schema);
        var itemProperties = document.RootElement
            .GetProperty("properties").GetProperty("parts")
            .GetProperty("items").GetProperty("properties");
        Assert.True(itemProperties.TryGetProperty("partRef", out _));
        Assert.True(itemProperties.TryGetProperty("placements", out _));
    }

    /// <summary>
    /// workstream H (2026-08-18 실사용 확인) — 벨트가 하의 렌더에도, 부츠가 다리 갑주 렌더에도,
    /// 버클·메달·파우치가 벨트 스트랩 렌더에도 그대로 남아 있었다(실제 생성 이미지로 확인).
    /// 머리/머리카락(workstream F)과 같은 근본 원인 — Decompose가 겹치는 파츠를 빼고
    /// 서술하라는 지시가 없어서 사진에 보이는 대로 다 옮겨 적은 것. description 규칙에
    /// "자기 occludedBy에 든 파츠는 빼고 서술" 을 명시해 일반화한다.
    ///
    /// workstream J (2026-08-18, 전체 점검) — 벨트가 workstream I에서 아예 흡수 전용으로
    /// 바뀌어서(별도 파츠로 절대 안 뽑힘) "벨트가 occludedBy에 든다"는 예시 자체가 죽은
    /// 코드가 됐다 — 부츠/갑주·갑옷 조각 등 여전히 살아있는 예시로 교체했다. 동시에 "겹침을
    /// 빼라"는 지시가 과하게 적용돼 파츠 자기 자신의 형태까지 생략될 위험을 사용자가 지적해,
    /// 반대쪽 안전장치("occludedBy에 없는 자기 자신의 형태는 생략하지 말 것")를 같이 넣었다.
    ///
    /// workstream K (2026-08-28, 사용자 요청) — 벨트가 다시 별도 파츠가 되면서(workstream K,
    /// Extract 변경) "벨트가 하의 위에 있다"는 예시가 다시 살아나 되돌려 넣었다.
    /// </summary>
    [Fact]
    public void CharacterDecompose_ExcludesOccludingSiblingsFromDescription()
    {
        var (system, _, _, _) = SeedPrompts.CharacterDecompose();

        // 배제 지시 — 벨트 예시가 되살아나고(workstream K), 부츠/머리카락 예시는 유지
        Assert.Contains("own occludedBy. A belt", system, StringComparison.Ordinal);
        Assert.Contains("covering the bottom of a leg armor", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hair covering the scalp", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("with every", system, StringComparison.OrdinalIgnoreCase);

        // 안전장치 — 배제가 이 파츠 자기 자신의 형태까지 생략하게 두지 않는다
        Assert.Contains("OCCLUDING part's own", system, StringComparison.Ordinal);
        Assert.Contains("never let it shrink THIS part's own form, material or", system, StringComparison.Ordinal);
        Assert.Contains("Describe this part's own shape in full, including the region an", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// workstream E §5 결정⑤ — 힌트 타입의 자연스러운 연장(벨트 부착물·다리 갑주)은
    /// 스코프 안, 힌트와 무관한 완전히 새 카테고리는 여전히 스코프 밖.
    ///
    /// workstream J (2026-08-18, 전체 점검) — 이 예외 규칙의 "발목 위 갑주" 예시는 이제
    /// workstream G(관절 경계 분리)·workstream I(갑옷 자동 분리)가 각자 더 구체적으로
    /// 다루고 있어서 세 번째로 같은 사례를 반복하고 있었다. 심지어 이름 짓는 예시가
    /// 서로 달랐다("다리 갑주" vs 관절 규칙의 "왼팔 갑주 팔꿈치 위") — 같은 대상에 다른
    /// 이름 짓기 지시가 동시에 걸려 있던 셈이라 모델을 헷갈리게 할 수 있었다. 그래서 이
    /// 규칙은 예시를 반복하지 않고 "그 규칙들과 같은 원칙"이라고만 짧게 참조하도록 줄였다.
    /// </summary>
    [Fact]
    public void CharacterExtract_ExplainsHintScopeExtensionWithConcreteCases()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        // 사례 1: 다리를 덮는 힌트가 있으면, 발목 위 갑주도 스코프 안(이슈 12) — 이제 관절·
        // 갑옷 규칙을 재참조하는 짧은 문구로 남아있다("다리 갑주" 중복 네이밍 예시는 제거)
        Assert.Contains("above the ankle", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("joint-boundary split and the armor auto-split", system, StringComparison.OrdinalIgnoreCase);

        // 중복 네이밍 예시(옛 문구)는 삭제됐다 — 지금은 관절 규칙 하나만 이름을 정한다
        Assert.DoesNotContain("Name it in Korean as its own part", system, StringComparison.Ordinal);

        // 사례 2: 완전히 새 카테고리는 여전히 금지(이슈 11과 충돌하지 않는다는 절충 문구)
        Assert.Contains("entirely unrelated", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garment category", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// workstream I (2026-08-18, 사용자 UI 결정) — 벨트·갑옷을 택소노미 힌트에서 뺐다(하의·
    /// 상의 위에 걸쳐지는 부위라 같이 힌트로 주면 렌더가 중복되던 문제, workstream H와 같은
    /// 근본 원인). 갑옷은 3D 리깅 가치가 있는 별도 오브젝트라 "갑옷" 자체를 힌트로 주지 않아도
    /// 상의/하의 힌트 안에서 자동으로 서브파츠로 나뉘게 남겨뒀다(가방→몸체/스트랩/버클과
    /// 같은 방식). 중복은 workstream H(occludedBy 배제 서술)가 막는다.
    ///
    /// workstream K (2026-08-28, 사용자 요청) — 벨트를 표면 장식 흡수(workstream I)에서
    /// 되돌려 갑옷과 같은 자동 분리 대상으로 바꿨다. 힌트로 벨트를 직접 고를 필요는 없지만
    /// (택소노미 UI는 그대로 두고), 하의·상의·원피스에 벨트가 보이면 항상 별도 파츠로
    /// 뽑고, 그 위에 붙은 버클·파우치 같은 부착물도 한 번 더 분리한다. 옷 파츠 자체는
    /// 벨트 없이 서술한다 — 실제 벨트 없는 이미지는 Generate 쪽 규칙이 보장한다.
    /// </summary>
    [Fact]
    public void CharacterExtract_SplitsBeltIntoItsOwnPartLikeArmor()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        // 갑옷 — 힌트된 어떤 파츠 위에도 자동으로 서브파츠로 나뉨 ("갑옷" 자체 힌트 불필요,
        // armor-any-region 로 "상의·하의"열거에서 "any hinted part" 원리로 일반화)
        Assert.Contains("within a hinted type's own scope", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fastened over any hinted part as a separate object", system, StringComparison.Ordinal);
        Assert.Contains("never hinted armor as its own type", system, StringComparison.OrdinalIgnoreCase);

        // 벨트 — 하의·원피스 위에서는 여전히 항상 별도 파츠, 옷은 벨트 없이 서술
        // (①-보강 3 로 상의는 제외됨 — CharacterExtract_SplitsBeltOnlyForBottomOrDress 가 그 경계를 확인)
        Assert.Contains("A belt worn", system, StringComparison.Ordinal);
        Assert.Contains("(하의, 원피스) is its own part too,", system, StringComparison.Ordinal);
        Assert.Contains("name it in Korean as \"벨트\"", system, StringComparison.Ordinal);
        Assert.Contains("sits on WITHOUT the belt", system, StringComparison.Ordinal);

        // 벨트 부착물(버클·파우치)도 별도 파츠
        Assert.Contains("clipped or strapped onto the belt is a", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// workstream G (2026-08-18 실사용 확인, 사용자 일반화 요청) — 처음엔 발목 위까지 올라가는
    /// 신발(workstream F)과 손목 위까지 올라가는 장갑을 각각 별도 규칙으로 다뤘는데, 사용자가
    /// "관절에 있는 부위는 관절 단위로 분리"라는 일반 원칙을 요구해 신발/장갑/갑주를 모두
    /// 아우르는 하나의 관절-경계 규칙으로 합쳤다. 발목·손목뿐 아니라 팔꿈치·무릎·어깨를 지나는
    /// 통짜 갑옷판도 대상이다 — 3D 리깅에서 관절 양쪽은 항상 독립된 두 파츠여야 하기 때문.
    /// </summary>
    [Fact]
    public void CharacterExtract_SplitsAnyCoveringAtMajorJoints()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        // 대상 관절 목록 — 발목·손목뿐 아니라 팔꿈치·무릎·어깨까지 일반화됐는지
        Assert.Contains("ankle, wrist, elbow, knee", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shoulder", system, StringComparison.OrdinalIgnoreCase);

        // 갑옷판처럼 딱딱한 소재도 대상 — "부드러운 의류만" 이 아니라는 반례
        Assert.Contains("rigid armor plate", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arm-guard that continues past the elbow", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("leg-guard that continues", system, StringComparison.OrdinalIgnoreCase);

        // 핵심 지시 — 이음매 없는 통짜 오브젝트라도 관절에서 둘로 쪼갠다
        Assert.Contains("split at that joint line", system, StringComparison.OrdinalIgnoreCase);

        // workstream M (2026-09-02) — 관절이 둘 이상이면 중간 구간에 이름이 두 개 생겼다.
        // 실 job 에서 "팔꿈치 아래" 와 "손목 위" 가 같은 팔뚝을 가리키며 함께 나왔다
        Assert.Contains("spans SEVERAL joints", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly THREE parts, not four", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("무릎~발목", system, StringComparison.Ordinal);
        Assert.Contains("must NOT appear", system, StringComparison.OrdinalIgnoreCase);

        // workstream N (2026-09-02) — 상의·하의 등 부드러운 의류(소프트웨어/스킨 메쉬)는 관절에서 쪼개지 않고
        // 단일 통짜 파츠로 유지(바지는 무릎에서 자르지 않고 허리~발목 1개, 상의는 어깨~손목 1개)
        Assert.Contains("EXCEPTION FOR SOFT GARMENTS", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tops, bottoms", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pants, long sleeves", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never cut at the knee", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never cut at the elbow", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 캐릭터 추출은 파츠 개수 상한을 명시한다 (리뷰 #3).
    ///
    /// 상한이 없으면 분리 장려 문구가 파츠를 무한정 늘려 4면 생성 비용이 폭증하고,
    /// 파츠가 100개를 넘으면 참조키 P100 이 분해 스키마의 ^P[0-9]{2}$ 를 위반해 구조적으로 죽는다.
    /// 참조키는 두 자리라 상한은 반드시 99 이하여야 한다.
    /// </summary>
    [Fact]
    public void CharacterExtract_BoundsPartCount()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        // 상한 숫자를 문구에서 찾아 두 자리 참조키 한계(≤99) 안인지 확인
        var numbers = System.Text.RegularExpressions.Regex.Matches(system, @"\d+")
            .Select(m => int.Parse(m.Value));
        Assert.Contains(numbers, n => n is > 1 and <= 99);

        // "entries" 상한 문장이 있다 — 기본 추출과 같은 형태의 개수 제한
        Assert.Contains("entries", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 캐릭터 생성은 흰(#ffffff) 누끼 배경과 성별 베이스바디를 지시하고 성별 변수를 받는다 (§D-04).
    /// 형제 파츠 화풍 일관성 지시도 유지한다 (Plan R-1, 리뷰 #4).
    /// </summary>
    [Fact]
    public void CharacterGenerate_FixesWhiteBackgroundAndGenderBaseBody()
    {
        var (system, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("{{gender}}", system, StringComparison.Ordinal);
        Assert.Contains("{{viewDirection}}", system, StringComparison.Ordinal);

        // 흰색 누끼 배경 — 배경 스튜디오의 회색(#F2F2F2)과 다르다 (§D-04)
        Assert.Contains("#ffffff", system, StringComparison.OrdinalIgnoreCase);

        // R-1 화풍 일관성 — 형제 파츠가 같은 화풍으로 보여야 한다 (기본 생성 프롬프트 계승)
        Assert.Contains("palette", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("belong together", system, StringComparison.OrdinalIgnoreCase);

        // 성별은 몸통뿐 아니라 의상 실루엣에 반영되고, 악세사리는 성별 무관 (재정의)
        Assert.Contains("silhouette", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gender-neutral", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// workstream F (2026-08-18 실사용 확인) — Extract는 머리/머리카락을 별개 파츠로 잘 뽑았는데도
    /// 머리 파츠 렌더 이미지에 머리카락이 그대로 남아 있었다. 원본 참조 사진에 머리카락이 있어도
    /// 무조건 빼라는 명시적 반례 문구를 추가한다.
    /// </summary>
    [Fact]
    public void CharacterGenerate_ExcludesHairFromHeadEvenWhenReferenceShowsIt()
    {
        var (system, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("when the reference image shows the character with hair", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("erase every strand", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// workstream K (2026-08-28, 사용자 요청) — 벨트가 다시 별도 파츠가 됐으니(Extract 변경),
    /// 하의/겉옷/원피스 렌더가 벨트 없이 나와야 한다. 원본 사진에 벨트가 보여도 무조건 빼라는
    /// 명시적 반례 문구를 머리카락 제외 규칙과 같은 방식으로 추가한다.
    /// </summary>
    [Fact]
    public void CharacterGenerate_ExcludesBeltFromBottomEvenWhenReferenceShowsIt()
    {
        var (system, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("draw it with NO belt at the waist even", system, StringComparison.Ordinal);
        Assert.Contains("erase the belt", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("belts", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// character-tuning 개선이 시드 본문에 담긴다 — Extract(머리/머리카락 분해 강화),
    /// Generate(A포즈·얼굴 방향·머리 민머리), Decompose(배치 좌표 좌상단 명확화 · workstream C).
    /// </summary>
    [Fact]
    public void CharacterPrompts_CarryTuningWording()
    {
        var (extract, _, _, _) = SeedPrompts.CharacterExtract();
        var (decompose, _, _, _) = SeedPrompts.CharacterDecompose();
        var (generate, _, _, _) = SeedPrompts.CharacterGenerate();

        // Extract — 머리/머리카락은 스코프 안이면 항상 별개 (①-보강 2 로 조건절화)
        Assert.Contains("When the head or the hair is in scope, they are two", extract, StringComparison.Ordinal);

        // Generate — A포즈·머리 민머리·측후면 얼굴 금지
        Assert.Contains("A-pose", generate, StringComparison.Ordinal);
        Assert.Contains("bare head", generate, StringComparison.Ordinal);
        Assert.Contains("Never leave the face or eyes turned toward the camera", generate, StringComparison.Ordinal);

        // Decompose — 배치 좌표 좌상단 명확화 (C)
        Assert.Contains("TOP-LEFT corner", decompose, StringComparison.Ordinal);
    }

    /// <summary>
    /// 캐릭터 시드 본문은 해당 단계의 허용 변수만 쓴다 (리뷰 #6).
    ///
    /// 마이그레이션 raw SQL INSERT 는 저장 검증(FindUnknownVariable)을 우회하므로, 시드에 오타
    /// 변수가 섞이면 배포 후 그 단계 실행이 전부 죽고 나서야 드러난다. 여기서 시드 본문을
    /// 실제 허용 변수로 검사해 앞당겨 잡는다.
    /// </summary>
    [Theory]
    [InlineData(TaskKind.Extract)]
    [InlineData(TaskKind.Decompose)]
    [InlineData(TaskKind.Generate)]
    public void CharacterSeedBodies_UseOnlyAllowedVariables(TaskKind kind)
    {
        var (system, user, _, _) = kind switch
        {
            TaskKind.Extract => SeedPrompts.CharacterExtract(),
            TaskKind.Decompose => SeedPrompts.CharacterDecompose(),
            TaskKind.Generate => SeedPrompts.CharacterGenerate(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Null(Noxtend.Domain.Prompt.PromptTemplate.FindUnknownVariable(system, LlmOperation.FromTask(kind)));
        Assert.Null(Noxtend.Domain.Prompt.PromptTemplate.FindUnknownVariable(user, LlmOperation.FromTask(kind)));
    }

    // ─────────────────────────── armor-any-region (2026-09-08) ───────────────────────────

    /// <summary>
    /// ① Extract — "따로 고정된 강체" 규칙이 상의·하의 열거가 아니라 "힌트된 어떤 파츠"
    /// 원리로 일반화됐는지. 갑주 부츠처럼 몸체 자체가 판인 경우는 분리 대상이 아니라는
    /// 반례("armored shaft")도 같이 있어야 한다 — 없으면 갑주 부츠가 둘로 쪼개진다(함정 3).
    /// </summary>
    [Fact]
    public void CharacterExtract_SplitsRigidPiecesFastenedOverAnyHintedPart()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        Assert.Contains("fastened over any hinted part", system, StringComparison.Ordinal);
        Assert.Contains("armored shaft", system, StringComparison.OrdinalIgnoreCase);

        // 따옴표 안 줄바꿈 회귀 — NAMING 규칙("Keep the spacing exactly as shown …
        // single spaces")과 어긋난다. 통짜 문자열이라 줄바꿈이 끼면 이 단언이 실패한다
        Assert.Contains("\"오른쪽 발목 갑주\"", system, StringComparison.Ordinal);

        // 관절 분할 규칙과의 경계 — 캡 종류 가드는 자르지 않는다 (리뷰 발견 4)
        Assert.Contains("caps a single joint", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// ②-2 Decompose·Generate — 관절 캡 조건이 골든셋 부위 열거("shoulder guard or
    /// pauldron, an elbow or knee guard")에서 "any joint the rig will bend" 원리로
    /// 일반화되고, 비관절 부위는 판 형태를 유지한다는 문장이 뒤따라야 한다. 두 곳
    /// 동시 변경 — 한 곳만 바꾸면 서술과 작화가 어긋난다(스펙 ②-2).
    /// </summary>
    [Fact]
    public void CharacterDecomposeAndGenerate_GeneralizeJointCapToAnyJoint()
    {
        var (decompose, _, _, _) = SeedPrompts.CharacterDecompose();
        var (generate, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("any joint", decompose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-joint", decompose, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("any joint", generate, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("non-joint", generate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// #11 회귀 방지 — ②-2 가 어깨 캡 규칙(관절을 감싸는 캡 형태)을 약화시키면 안 된다
    /// (스펙 예상 함정 4). 관절 조건은 비관절 문장을 덧붙이기만 해야 하므로 기존 핵심
    /// 문구가 그대로 남아 있는지 확인한다.
    /// </summary>
    [Fact]
    public void CharacterDecomposeAndGenerate_KeepTheShoulderCapRuleIntact()
    {
        var (decompose, _, _, _) = SeedPrompts.CharacterDecompose();
        var (generate, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("closing into a cap", decompose, StringComparison.Ordinal);
        Assert.Contains("closed cap wrapping the joint", generate, StringComparison.Ordinal);
    }

    /// <summary>
    /// ②-3 Generate — 벨트·머리카락 반례의 문법을 일반화한 문단이 있는지. 판단 기준은
    /// "이 파츠 서술에 없는 것"이다(Generate 는 가림 정보를 받지 않는다 — 스펙 함정 12).
    /// </summary>
    [Fact]
    public void CharacterGenerate_GeneralizesOccludingLayerExclusion()
    {
        var (system, _, _, _) = SeedPrompts.CharacterGenerate();

        Assert.Contains("NOT in this part's own description", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// ②-1 RewriteDescriptions — NEW 파츠의 부위를 이름·좌표·힌트로 교차 확인하라는
    /// 지시가 System 에, {{partHints}} 자리표시자가 User 에 있는지.
    /// </summary>
    [Fact]
    public void RewriteDescriptions_CarriesPartHintsForJointJudgement()
    {
        var (system, user, _, _) = SeedPrompts.RewriteDescriptions();

        Assert.Contains("part hints", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("joint", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{{partHints}}", user, StringComparison.Ordinal);
    }

    /// <summary>
    /// ②-1 보강 (2026-09-09 실 API 실측) — 검수에서 그린 `왼쪽 어깨 스카프`(화면 왼쪽 = 착용자
    /// 오른팔, 원본은 주황색 천 매듭)의 서술이 "짙은 청색 천 스카프"로 나왔다. 모델이 이름의
    /// "왼쪽"을 착용자 기준으로 읽고 화면 반대쪽(청색 갑주)을 보고 썼다 — 좌우 기준과 이름·
    /// 좌표 충돌 시 우선순위가 문면에 없었다.
    /// </summary>
    [Fact]
    public void RewriteDescriptions_PrefersRegionCoordinatesOverTheNameSide()
    {
        var (system, _, _, _) = SeedPrompts.RewriteDescriptions();

        Assert.Contains("wearer's left and right", system, StringComparison.Ordinal);
        Assert.Contains("The region coordinates win", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// ①-보강 2 (2026-09-09 실 API 실측, F96BE0F7) — 힌트가 상의·하의·신발·무기·장갑
    /// 5종뿐인데 Extract 가 힌트에 없는 베이스바디·머리·머리카락을 냈다. "항상 한 파츠",
    /// "ALWAYS two separate parts … 반환하라" 는 강제 반환 문구가 힌트 스코프 규칙보다
    /// 앞서 읽혀 스코프를 이겼다 — 조건절로 바꿔 "나눌 때 어떻게" 규칙만 남긴다.
    /// </summary>
    [Fact]
    public void CharacterExtract_LetsHintScopeWinOverAlwaysRules()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        Assert.Contains("when the base body is in scope", system, StringComparison.Ordinal);
        Assert.Contains("When the head or the hair is in scope", system, StringComparison.Ordinal);
        Assert.DoesNotContain("are ALWAYS two separate parts", system, StringComparison.Ordinal);
        Assert.DoesNotContain("Return them as distinct entries", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// ①-보강 3 (같은 실측) — 힌트에 벨트가 없고 하의만 있었는데 벨트 5종(벨트·버클·파우치
    /// 2·메달)이 딸려 나왔다. "상의, 하의, 원피스" 열거 때문에 상의만 힌트해도 벨트가
    /// 자동 분리 대상이 됐다 — 하의·원피스로 좁힌다. 갑주 자동 분리는 그대로 둔다(택소노미에
    /// "갑옷" 이 없어 같은 방식으로 조건화할 수 없다 — 스펙 참고).
    /// </summary>
    [Fact]
    public void CharacterExtract_SplitsBeltOnlyForBottomOrDress()
    {
        var (system, _, _, _) = SeedPrompts.CharacterExtract();

        Assert.Contains("over a hinted bottom or dress (하의, 원피스)", system, StringComparison.Ordinal);
        Assert.DoesNotContain("hinted top, bottom or dress", system, StringComparison.Ordinal);
    }
}
