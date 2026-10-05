using System.Text.Json;
using System.Text.Json.Serialization;
using Noxtend.Domain.Job;

namespace Noxtend.Infrastructure.Persistence.Serialization;

/// <summary>
/// 3D 재구성 공정이 얼린 네 방향 이미지의 JSON 표현.
///
/// Design Ref: §4.3
///
/// **이 값이 열 넷이 아니라 JSON 인 이유**는 네 ID 로 검색·정렬할 일이 없어서다. 열로
/// 쪼개면 3D 를 만들지 않는 작업의 모든 공정 행에 빈 열 넷이 생기고, 두 번째 공급자가
/// 다른 입력 구성을 요구할 때 다시 마이그레이션해야 한다.
/// </summary>
internal static class MeshInputSetJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(MeshInputSet inputs)
        => JsonSerializer.Serialize(inputs, Options);

    public static MeshInputSet? Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<MeshInputSet>(json, Options);
}
