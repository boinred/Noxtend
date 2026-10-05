using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Noxtend.Application.Job;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Stages;

/// <summary>
/// 공정 0 — 장면 분석.
///
/// Design Ref: §2.2 · Plan D-12
///
/// **앞 공정이 없으므로 변수도 없다.** 이미지만 보고 이후 모든 단계의 기준선을 만든다.
/// </summary>
public sealed class AnalyzeStage : IStage
{
    public TaskKind Kind => TaskKind.Analyze;

    public IReadOnlyDictionary<string, string> BuildVariables(PipelineJob job)
        => new Dictionary<string, string>();

    public Action<PipelineJob> Interpret(PipelineJob job, string rawJson)
    {
        using var document = StageJson.Parse(rawJson);
        var root = document.RootElement;

        // camera·light·scaleReference 가 없으면 여기서 걸린다 — 이 셋이
        // 조립을 좌우하므로 나머지가 다 있어도 쓸 수 없다 (FR-05).
        //
        // **`SCENE_INCOMPLETE` 로 나가야 한다.** "JSON 이 깨졌다" 와 "형식은 맞는데
        // 필수 필드가 없다" 는 사용자가 할 일이 다르다 — 전자는 재시도, 후자는 프롬프트 수정
        var camera = RequiredSceneObject(root, "camera");
        var light = RequiredSceneObject(root, "light");
        var scale = RequiredSceneObject(root, "scaleReference");

        var scene = new SceneSpec(
            ReadPalette(root),
            StageJson.RequiredString(root, "timeOfDay"),
            StageJson.RequiredString(root, "mood"),
            StageJson.RequiredString(root, "renderingStyle"),
            StageJson.RequiredString(root, "materialFeel"),
            new CameraSpec(
                StageJson.RequiredString(camera, "type"),
                StageJson.RequiredString(camera, "eyeLevel"),
                StageJson.RequiredNumber(camera, "horizonY")),
            new LightSpec(
                StageJson.RequiredString(light, "direction"),
                StageJson.RequiredString(light, "temperature"),
                StageJson.RequiredString(light, "shadowHardness")),
            new ScaleReference(
                StageJson.RequiredString(scale, "object"),
                StageJson.RequiredString(scale, "realWorldSize"),
                ReadHeightMeters(scale, required: job.Category == AssetCategory.Background)));

        return target => target.ApplyScene(scene);
    }

    /// <summary>배경 조립 기준 높이 — 다른 category의 과거 계약은 nullable 유지.</summary>
    private static double? ReadHeightMeters(JsonElement scale, bool required)
    {
        if (!scale.TryGetProperty("heightMeters", out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                throw new ProviderBadResponseException("heightMeters 가 없습니다");
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var heightMeters) ||
            !double.IsFinite(heightMeters) || heightMeters is <= 0 or > MaxScaleHeightMeters)
        {
            throw new ProviderBadResponseException(
                $"heightMeters 는 0보다 크고 {MaxScaleHeightMeters} 이하인 숫자여야 합니다");
        }

        return heightMeters;
    }

    /// <summary>
    /// 구조화 팔레트 읽기 (Design §5.3 · FR-01~05).
    ///
    /// **범용 배열 헬퍼를 만들지 않고 여기 둔다.** 규칙이 팔레트 하나에만 붙어 있고,
    /// 추상화하면 오류 메시지에서 "몇 번째 항목의 어느 필드" 가 사라진다.
    ///
    /// 위반은 전부 <see cref="ProviderBadResponseException"/> 이다 — 구조화 출력 스키마를
    /// 준 뒤에도 어긋난 것이므로 공급자가 계약을 어긴 것이고, 다시 돌리면 대개 맞는다.
    /// 메시지에는 모델 응답 원문을 담지 않고 위치와 필드만 적는다.
    /// </summary>
    private static IReadOnlyList<PaletteEntry> ReadPalette(JsonElement root)
    {
        if (!root.TryGetProperty("palette", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderBadResponseException("palette 배열이 없습니다");
        }

        var length = array.GetArrayLength();
        if (length is < MinPaletteEntries or > MaxPaletteEntries)
        {
            throw new ProviderBadResponseException(
                $"palette 는 {MinPaletteEntries}~{MaxPaletteEntries}개여야 합니다 (받은 값 {length}개)");
        }

        var entries = new List<PaletteEntry>(length);
        var index = 0;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ProviderBadResponseException($"palette[{index}] 이 객체가 아닙니다");
            }

            var name = ReadPaletteString(item, index, "name").Trim();
            if (name.Length == 0)
            {
                throw new ProviderBadResponseException($"palette[{index}].name 이 비어 있습니다");
            }

            var hex = ReadPaletteString(item, index, "hex");
            if (!HexPattern.IsMatch(hex))
            {
                throw new ProviderBadResponseException($"palette[{index}].hex 형식이 올바르지 않습니다");
            }

            // 대문자로 굳힌다 — 같은 색이 두 표기로 남으면 비교도 중복 제거도 안 된다
            entries.Add(new PaletteEntry(name, hex.ToUpperInvariant()));
            index++;
        }

        return entries;
    }

    private static string ReadPaletteString(JsonElement item, int index, string field)
    {
        if (!item.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new ProviderBadResponseException($"palette[{index}].{field} 이 없습니다");
        }

        return value.GetString() ?? string.Empty;
    }

    /// <summary>단색 장면의 명암 변형을 담으면서 한 줄 UI 가 과하게 늘어나지 않는 범위 (D-04).</summary>
    private const int MinPaletteEntries = 3;

    private const int MaxPaletteEntries = 8;

    private const double MaxScaleHeightMeters = 10_000;

    /// <summary>sRGB 6자리만 받는다 (D-03). 짧은 표기나 알파 채널은 CSS 와 프롬프트 양쪽을 흔든다.</summary>
    private static readonly Regex HexPattern = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// 장면 필수 객체. 없으면 <see cref="SceneValidationException"/> 이다.
    ///
    /// <see cref="StageJson.RequiredObject"/> 는 `PROVIDER_BAD_RESPONSE` 로 이어지므로
    /// 여기서만 다른 예외를 쓴다 — 나머지 필드(분위기·재질 등)는 없어도 다음 단계가
    /// 돌아가므로 형식 오류로 다뤄도 된다.
    /// </summary>
    private static JsonElement RequiredSceneObject(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            throw new SceneValidationException(name);
        }

        return element;
    }
}

/// <summary>
/// 공정 1 — 파츠 식별.
///
/// 사이클 #4 의 추출에서 일관성 프롬프트 부분이 빠진 형태다. 장면 공정이 그것을 가져갔다.
/// </summary>
public sealed class ExtractStage : IStage
{
    public TaskKind Kind => TaskKind.Extract;

    public IReadOnlyDictionary<string, string> BuildVariables(PipelineJob job)
        => new Dictionary<string, string>
        {
            ["scene"] = StageJson.Describe(RequireScene(job)),
            // 캐릭터 고유 변수 — 종류 힌트는 여기(추출)로 흐른다 (character-studio §D-03). 키는 항상 넣는다
            ["gender"] = CharacterVariables.GenderText(job.Gender),
            ["partHints"] = PartHintCodec.Render(job.PartHints),
        };

    public Action<PipelineJob> Interpret(PipelineJob job, string rawJson)
    {
        using var document = StageJson.Parse(rawJson);

        var names = StageJson.StringArray(document.RootElement, "parts", required: true);
        if (names.Count == 0)
        {
            throw new ProviderBadResponseException("parts 가 비어 있습니다");
        }

        // 배경 실제 크기 기준 이름 — 부분 일치 허용 시 조립기가 다른 파츠를 기준으로 삼는다
        if (job.Category == AssetCategory.Background &&
            job.Scene?.Scale.HeightMeters is not null &&
            !names.Contains(job.Scene.Scale.Object, StringComparer.Ordinal))
        {
            throw new ProviderBadResponseException(
                "parts 에 scaleReference.object 와 정확히 같은 기준 파츠가 없습니다");
        }

        return target => target.ApplyParts(names);
    }

    internal static SceneSpec RequireScene(PipelineJob job)
        => job.Scene ?? throw new ProviderCallFailedException(
            "장면 명세가 없습니다 — 앞 공정이 끝나지 않았습니다");
}

/// <summary>
/// 공정 2 — 파츠 분해.
///
/// Design Ref: Plan D-1·D-2
///
/// **이미지 1장에 호출 1번이다.** 가림 관계는 파츠들 사이의 정보라 파츠별로 나눠
/// 부르면 각 호출이 다른 파츠를 몰라서 낼 수 없다.
/// </summary>
public sealed class DecomposeStage : IStage
{
    public TaskKind Kind => TaskKind.Decompose;

    public IReadOnlyDictionary<string, string> BuildVariables(PipelineJob job)
        => new Dictionary<string, string>
        {
            ["scene"] = StageJson.Describe(ExtractStage.RequireScene(job)),
            // 짧은 참조 키 — 자연어 이름 축약이 파츠 관계를 끊지 않게 하는 모델용 식별자
            ["parts"] = string.Join(
                "\n", job.Parts
                    .OrderBy(p => p.Ordinal)
                    .Select(p => $"{ReferenceFor(p)}: {p.Name}")),
            // 캐릭터 고유 변수 — 개수 힌트는 여기(분해)로 흘러 파츠별 placements 기대치가 된다 (§D-03)
            ["gender"] = CharacterVariables.GenderText(job.Gender),
            ["partHints"] = PartHintCodec.Render(job.PartHints),
        };

    public Action<PipelineJob> Interpret(PipelineJob job, string rawJson)
    {
        using var document = StageJson.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderBadResponseException("parts 배열이 없습니다");
        }

        // 모델용 참조 키를 도메인의 정식 파츠 이름으로 복원하는 변환표
        var references = job.Parts.ToDictionary(ReferenceFor, part => part.Name);
        var details = new List<PartDetail>();

        foreach (var part in parts.EnumerateArray())
        {
            var partReference = StageJson.RequiredString(part, "partRef");

            // 주어진 목록 밖 partRef(모델이 만든 여분·재번호 파츠)는 버린다 — 필수 파츠 누락은
            // 반영 전 도메인 검증(ValidatePartDetails)이 개수 불일치로 잡는다. 여기서 던지면
            // 파츠 힌트로 일부만 고른 정상 입력까지 죽는다 (character-tuning workstream B)
            if (!references.TryGetValue(partReference, out var partName))
            {
                continue;
            }

            // occludedBy 의 목록 밖 참조는 버린다 — 가림 정보는 생성 프롬프트에 안 들어가므로
            // (PromptTemplate §3.5) 세트 밖 가림 파츠를 흘려도 생성에 영향이 없다. 힌트로 일부만
            // 고르면 몸통이 제외한 옷에 가려지는 것이 정상이다
            var occludedByNames = StageJson.StringArray(part, "occludedBy", required: false)
                .Where(references.ContainsKey)
                .Select(reference => references[reference])
                .ToList();

            details.Add(new PartDetail(
                partName,
                StageJson.RequiredString(part, "category"),
                StageJson.RequiredString(part, "description"),
                ReadPlacements(part, partName),
                StageJson.RequiredInt(part, "depthOrder"),
                occludedByNames,
                ReadSurface(part)));
        }

        // 유효성 검사는 엔티티가 한다 — 규칙이 한 곳에 있어야 흩어지지 않는다.
        // **반영 전에** 검사해야 실패가 공정 실패로 이어진다
        job.ValidatePartDetails(details);

        return target => target.ApplyPartDetails(details);
    }

    /// <summary>
    /// 표면 표시 읽기 (#20 §4.1).
    ///
    /// **없으면 낱개 물건이다.** 사이클 #20 이전 프롬프트로 돈 작업에는 이 필드가 없고,
    /// 그 결과가 지금까지의 동작이다. 모르는 값도 같은 자리로 보낸다 — 표면으로 잘못
    /// 눕히는 것보다 세워 두는 쪽이 되돌리기 쉽다.
    /// </summary>
    private static PartSurface ReadSurface(JsonElement part)
        => StageJson.OptionalString(part, "surface") switch
        {
            "ground" => PartSurface.Ground,
            "vertical" => PartSurface.Vertical,
            _ => PartSurface.None,
        };

    /// <summary>작업 안에서 안정적인 1부터 시작하는 모델용 파츠 참조.</summary>
    /// <summary>
    /// 배치 목록 읽기 (Design §5.3).
    ///
    /// **개수는 여기서 세지 않는다.** 1~20 규칙은 도메인이 판단한다 — 형식과 의미를 나눠 두는
    /// 기존 구조를 지킨다. 여기가 보는 것은 "배열인가, 항목이 객체인가, 네 수가 있는가" 뿐이다.
    ///
    /// 메시지에는 모델 응답 원문을 담지 않고 파츠 이름과 위치만 적는다.
    /// </summary>
    private static IReadOnlyList<Bounds> ReadPlacements(JsonElement part, string partName)
    {
        if (!part.TryGetProperty("placements", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderBadResponseException($"'{partName}' 의 placements 배열이 없습니다");
        }

        var placements = new List<Bounds>(array.GetArrayLength());
        var index = 0;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ProviderBadResponseException(
                    $"'{partName}' 의 placements[{index}] 이 객체가 아닙니다");
            }

            var placement = new Bounds(
                StageJson.RequiredNumber(item, "x"),
                StageJson.RequiredNumber(item, "y"),
                StageJson.RequiredNumber(item, "w"),
                StageJson.RequiredNumber(item, "h"));

            if (IsInsideFrame(placement))
            {
                placements.Add(placement);
            }

            index++;
        }

        return placements;
    }

    /// <summary>
    /// 화면을 벗어나는 배치는 **버린다.**
    ///
    /// **자르지 않는 이유는 배치가 합성 지시이기 때문이다.** 배치는 "이 에셋을 이 자리에 이
    /// 크기로 놓아라" 인데, 가장자리에 걸친 상자를 잘라 넣으면 온전한 그림을 반쪽 상자에 밀어
    /// 넣게 된다 — 합성 결과가 눌린다. 부분적으로 보이는 개체는 재사용 에셋을 놓을 자리가
    /// 아니다.
    ///
    /// **버리는 것이 필요해진 것은 배치가 늘면서다.** 파츠당 상자가 하나이던 시절엔 작업당
    /// 9개라 경계 초과가 드물었는데, 84~95개가 되자 세 번 연속 실패했다 — 매번 다른 상자가
    /// 넘쳤고 초과량은 1~7%였다. 재시도로 넘을 수 있는 벽이 아니다.
    ///
    /// **조용히 사라지지는 않는다.** 한 파츠의 배치가 전부 버려지면 0개가 되고, 도메인이
    /// 그것을 거부한다 (최소 1개). 모델이 좌표계를 통째로 잘못 이해한 경우는 그렇게 드러난다.
    /// 버리기 전 원본은 `LlmCalls.ResponsePayload` 에 그대로 남는다.
    /// </summary>
    private static bool IsInsideFrame(Bounds bounds)
        => bounds.IsWithinFrame();

    private static string ReferenceFor(AssetPart part) => $"P{part.Ordinal + 1:D2}";
}

/// <summary>
/// 공정 2.5 — 서술 재작성 (occludedby-recompute §입력→출력 3).
///
/// Design Ref: occludedby-recompute §목표
///
/// **좌표를 묻지 않는다.** 사람이 검수 화면에서 그린 좌표가 이미 확정이고, 가림 관계도
/// 코드가 정했다. 여기서 물을 것은 "가린 부분을 뺀 서술" 하나뿐이라 응답 스키마에
/// bounds 가 없다 — 모델이 좌표를 바꿀 방법 자체가 없다.
///
/// **승인당 한 번이다.** 대상 파츠를 묶어 한 번에 부른다.
/// </summary>
public sealed class RewriteDescriptionsStage : IStage
{
    public TaskKind Kind => TaskKind.RewriteDescriptions;

    public IReadOnlyDictionary<string, string> BuildVariables(PipelineJob job)
        => new Dictionary<string, string>
        {
            ["scene"] = StageJson.Describe(ExtractStage.RequireScene(job)),
            // 종류 힌트 — NEW 파츠(사람이 방금 그린 서술 없는 파츠)의 부위 판정 교차 확인용 (armor-any-region)
            ["partHints"] = PartHintCodec.Render(job.PartHints),
            // 대상마다 현재 서술과 "무엇에 가려지는가"를 함께 준다 — 가리는 파츠의 서술이
            // 있어야 모델이 어느 영역을 빼야 하는지 안다
            ["targets"] = string.Join("\n", job.DescriptionsStale.Select(name => Describe(job, name))),
        };

    public string BuildJsonSchema(PipelineJob job, string promptSchema)
    {
        var root = JsonNode.Parse(promptSchema)?.AsObject()
            ?? throw new ProviderBadResponseException("RewriteDescriptions JSON Schema가 객체가 아닙니다");
        var nameSchema = root["properties"]?["parts"]?["items"]?["properties"]?["name"] as JsonObject
            ?? throw new ProviderBadResponseException("RewriteDescriptions JSON Schema에 name 속성이 없습니다");
        var allowedNames = new JsonArray();

        foreach (var name in job.DescriptionsStale)
        {
            allowedNames.Add(name);
        }

        nameSchema["enum"] = allowedNames;
        return root.ToJsonString();
    }

    private static string Describe(PipelineJob job, string name)
    {
        var part = job.Parts.Single(p => p.Name == name);

        // 서술이 없으면 사람이 방금 그린 파츠다 — 무엇을 다시 쓸지가 아니라 어디를 볼지를
        // 알려줘야 한다. 좌표는 여기 입력으로만 쓰이고 응답 스키마에는 없다
        if (string.IsNullOrWhiteSpace(part.Description))
        {
            var box = part.Placements[0];

            return $"- {name} (NEW, needs a description; category: {part.Category ?? "unknown"})\n" +
                   $"  region → x={box.X:0.###} y={box.Y:0.###} w={box.W:0.###} h={box.H:0.###}";
        }

        var occluders = string.Join(
            "; ",
            part.OccludedBy.Select(o => $"{o}: {job.Parts.Single(p => p.Name == o).Description}"));

        return $"- {name}: {part.Description}\n  covered by → {occluders}";
    }

    public Action<PipelineJob> Interpret(PipelineJob job, string rawJson)
    {
        using var document = StageJson.Parse(rawJson);

        if (!document.RootElement.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderBadResponseException("parts 배열이 없습니다");
        }

        var rawRewrites = parts
            .EnumerateArray()
            .Select(part => (
                Name: StageJson.RequiredString(part, "name"),
                Description: StageJson.RequiredString(part, "description"),
                // 사람이 카테고리를 안 쓴 파츠만 모델이 채운다 — 도메인이 덮어쓰기를 막는다
                Category: part.TryGetProperty("category", out var category)
                          && category.ValueKind == JsonValueKind.String
                    ? category.GetString()
                    : null))
            .ToList();

        var targets = job.DescriptionsStale.ToList();
        var known = job.Parts.Select(p => p.Name).ToHashSet();
        var targetNames = targets.ToHashSet(StringComparer.Ordinal);
        var matchedTargets = new HashSet<string>(StringComparer.Ordinal);
        var mismatched = new List<(string Name, string Description, string? Category)>();
        var rewrites = new List<(string Name, string Description, string? Category)>();

        foreach (var item in rawRewrites)
        {
            if (targetNames.Contains(item.Name))
            {
                if (!matchedTargets.Add(item.Name))
                {
                    throw new ProviderBadResponseException("재작성 대상 이름이 중복되었습니다");
                }

                rewrites.Add(item);
            }
            else if (!known.Contains(item.Name))
            {
                mismatched.Add(item);
            }
            else
            {
                rewrites.Add(item);
            }
        }

        var missingTargets = targets.Where(target => !matchedTargets.Contains(target)).ToList();
        if (mismatched.Count == 1 && missingTargets.Count == 1)
        {
            var item = mismatched[0];
            rewrites.Add((missingTargets[0], item.Description, item.Category));
        }
        else if (mismatched.Count > 0 || missingTargets.Count > 0)
        {
            throw new ProviderBadResponseException("요청한 재작성 대상과 응답 파츠 이름이 일치하지 않습니다");
        }

        // 반영 전에 검증한다 — 공정을 성공으로 확정한 뒤 실패하면 되돌릴 자리가 없다
        job.ValidateDescriptionRewrites(rewrites);

        return target => target.ApplyDescriptionRewrites(rewrites);
    }
}
