using System.Text.Json;
using System.Text.Json.Nodes;
using Noxtend.Domain.Job;

namespace Noxtend.Infrastructure.Persistence.Serialization;

/// <summary>
/// `SceneJson` 열의 읽기·쓰기 경계 (Design §6.2 · D-05).
///
/// **EF 설정 클래스가 아니라 여기가 자리인 이유**는 구형 형식 해석이 테스트 가능한
/// 애플리케이션 규칙이기 때문이다. 값 변환기 안에 두면 설정 클래스가 JSON 호환 책임까지
/// 떠안고 seam 이 사라진다.
///
/// **`JsonConverter` 를 전역 등록하지 않는다.** 그렇게 하면 레거시 승격 규칙이 API 직렬화나
/// 모델 응답 파싱에까지 새어 나가, "구형을 받아준다" 가 신규 계약에도 적용된다.
/// </summary>
internal static class SceneJsonSerializer
{
    /// <summary>한글이 \uXXXX 로 부풀면 열 크기가 3배가 된다.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>쓰기는 항상 새 형식이다 — 한 번 읽고 저장된 행은 자연히 승격된다 (D-07).</summary>
    public static string Serialize(SceneSpec scene)
        => JsonSerializer.Serialize(scene, JsonOptions);

    /// <summary>
    /// 읽기는 방어적이다.
    ///
    /// 값 변환기에서 예외가 나면 그 행을 건드리는 **모든 조회**가 터진다 — 작업 하나가
    /// 깨졌을 뿐인데 홈 목록 전체가 500 이 된다. 못 읽으면 "장면 없음" 으로 둔다.
    /// 원문은 내역(`LlmCalls`)에 남으므로 진단은 거기서 한다.
    /// </summary>
    public static SceneSpec? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            // 레거시 변환은 JSON 노드에서 한 번만 하고, 그 뒤는 평범한 역직렬화를 재사용한다
            var node = JsonNode.Parse(json);
            if (node is not JsonObject root)
            {
                return null;
            }

            PromoteLegacyPalette(root);

            return root.Deserialize<SceneSpec>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 구형 `string[]` 팔레트를 `{ Name, Hex }` 배열로 올린다 (§6.3).
    ///
    /// 신규 형식이면 손대지 않는다. **신규 응답의 3~8개 규칙은 여기 적용하지 않는다** —
    /// 과거 데이터 보존이 목적이고, 소급하면 기존 장면이 통째로 사라진다 (§4.3).
    /// </summary>
    private static void PromoteLegacyPalette(JsonObject root)
    {
        var key = root.ContainsKey("Palette") ? "Palette" : "palette";

        if (root[key] is not JsonArray array)
        {
            return;
        }

        var promoted = new JsonArray();

        foreach (var item in array)
        {
            // 이미 새 형식이면 그대로 옮긴다. 섞여 있는 배열도 있을 수 있다
            if (item is JsonObject entry)
            {
                promoted.Add(entry.DeepClone());
                continue;
            }

            if (item is not JsonValue value || value.GetValueKind() != JsonValueKind.String)
            {
                continue;
            }

            if (LegacyPaletteColorResolver.Promote(value.GetValue<string>()) is not { } result)
            {
                continue;
            }

            promoted.Add(new JsonObject
            {
                ["Name"] = result.Name,
                ["Hex"] = result.Hex,
            });
        }

        root[key] = promoted;
    }
}
