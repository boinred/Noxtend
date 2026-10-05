using System.Text.Json;
using Noxtend.Domain.Scene;
using Noxtend.Domain.Similarity;

namespace Noxtend.Application.Similarity;

/// <summary>공급자 응답이 계약을 어겼다 — SIMILARITY_EVALUATION_INVALID 로 옮겨진다.</summary>
public sealed class SimilarityEvaluationFormatException(string message) : Exception(message);

public sealed record ParsedSimilarityEvaluation(
    IReadOnlyList<SimilarityDimension> Dimensions,
    IReadOnlyList<SimilarityAdjustment> Adjustments,
    IReadOnlyList<string> RegenerationNotes);

/// <summary>
/// 평가 응답 해석 (§5.4 · §6).
///
/// Design Ref: background-similarity-tuning §5.3~5.4
///
/// **모델 출력은 비신뢰 입력이다** (§13). 스키마가 강제돼 있어도 여기서 다시 검증한다 —
/// 여섯 축·enum·범위는 도메인(SimilarityScore·SceneAdjuster)이 최종 판정하고, 이 파서는
/// JSON 을 도메인 타입으로 옮기며 모양 위반을 계약 위반 예외로 바꾼다.
/// 모델이 보낸 overallScore 는 읽지 않는다 (D-05).
/// </summary>
public static class SimilarityEvaluationParser
{
    public static ParsedSimilarityEvaluation Parse(string rawJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Strip(rawJson));
        }
        catch (JsonException)
        {
            throw new SimilarityEvaluationFormatException("응답이 JSON 이 아닙니다");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SimilarityEvaluationFormatException("응답 최상위가 객체가 아닙니다");
            }

            return new ParsedSimilarityEvaluation(
                ReadDimensions(root),
                ReadAdjustments(root),
                ReadNotes(root));
        }
    }

    private static IReadOnlyList<SimilarityDimension> ReadDimensions(JsonElement root)
    {
        if (!root.TryGetProperty("dimensions", out var dimensions)
            || dimensions.ValueKind != JsonValueKind.Array)
        {
            throw new SimilarityEvaluationFormatException("dimensions 배열이 없습니다");
        }

        var result = new List<SimilarityDimension>();
        foreach (var item in dimensions.EnumerateArray())
        {
            var kindText = RequiredString(item, "kind");
            if (!Enum.TryParse<SimilarityDimensionKind>(kindText, ignoreCase: true, out var kind))
            {
                throw new SimilarityEvaluationFormatException($"알 수 없는 축입니다: {kindText}");
            }

            result.Add(new SimilarityDimension(
                kind,
                RequiredInt(item, "score"),
                RequiredString(item, "evidence"),
                RequiredString(item, "recommendation")));
        }

        return result;
    }

    private static IReadOnlyList<SimilarityAdjustment> ReadAdjustments(JsonElement root)
    {
        if (!root.TryGetProperty("adjustments", out var adjustments))
        {
            return [];
        }

        if (adjustments.ValueKind != JsonValueKind.Array)
        {
            throw new SimilarityEvaluationFormatException("adjustments 가 배열이 아닙니다");
        }

        var result = new List<SimilarityAdjustment>();
        foreach (var item in adjustments.EnumerateArray())
        {
            // allowlist 여섯 형태만 — 임의 property path 는 여기서 죽는다 (D-06)
            var type = RequiredString(item, "type");
            SceneAdjustmentCommand command = type switch
            {
                "scaleScene" => new ScaleScene(RequiredDouble(item, "factor")),
                "moveInstance" => new MoveInstance(
                    RequiredGuid(item, "partId"), RequiredInt(item, "ordinal"),
                    RequiredDouble(item, "deltaX"), RequiredDouble(item, "deltaZ")),
                "rotateInstance" => new RotateInstance(
                    RequiredGuid(item, "partId"), RequiredInt(item, "ordinal"),
                    RequiredDouble(item, "deltaDegrees")),
                "scaleInstance" => new ScaleInstance(
                    RequiredGuid(item, "partId"), RequiredInt(item, "ordinal"),
                    RequiredDouble(item, "factor")),
                "adjustCamera" => new AdjustCamera(
                    RequiredDouble(item, "yawDeltaDegrees"),
                    RequiredDouble(item, "pitchDeltaDegrees"),
                    RequiredDouble(item, "distanceFactor"),
                    RequiredDouble(item, "fovDeltaDegrees")),
                "adjustLight" => new AdjustLight(
                    RequiredDouble(item, "azimuthDeltaDegrees"),
                    RequiredDouble(item, "elevationDeltaDegrees"),
                    RequiredDouble(item, "intensityFactor")),
                _ => throw new SimilarityEvaluationFormatException($"허용되지 않는 보정입니다: {type}"),
            };

            result.Add(new SimilarityAdjustment(
                Guid.NewGuid(), command,
                RequiredDouble(item, "confidence"),
                RequiredString(item, "reason")));
        }

        return result;
    }

    private static IReadOnlyList<string> ReadNotes(JsonElement root)
    {
        if (!root.TryGetProperty("regenerationNotes", out var notes))
        {
            return [];
        }

        if (notes.ValueKind != JsonValueKind.Array)
        {
            throw new SimilarityEvaluationFormatException("regenerationNotes 가 배열이 아닙니다");
        }

        return [.. notes.EnumerateArray().Select(note =>
            note.ValueKind == JsonValueKind.String
                ? note.GetString()!
                : throw new SimilarityEvaluationFormatException("재생성 노트는 문자열이어야 합니다"))];
    }

    // ─── 필드 읽기 — 숫자 문자열·NaN 거부 (§5.4) ───

    private static string RequiredString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new SimilarityEvaluationFormatException($"{name} 문자열이 없습니다");

    private static int RequiredInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new SimilarityEvaluationFormatException($"{name} 정수가 없습니다");

    private static double RequiredDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var parsed)
            && double.IsFinite(parsed)
            ? parsed
            : throw new SimilarityEvaluationFormatException($"{name} 수가 없습니다");

    private static Guid RequiredGuid(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : throw new SimilarityEvaluationFormatException($"{name} id 가 없습니다");

    /// <summary>코드펜스 방어 — 구조화 출력이 강제돼도 방어용으로만 남긴다 (기존 파서와 같은 규칙).</summary>
    private static string Strip(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                return trimmed[(firstLineEnd + 1)..lastFence];
            }
        }

        return trimmed;
    }
}
