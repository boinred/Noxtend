using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;

namespace Noxtend.Tuning.Domain.Prompt;

/// <summary>
/// 프롬프트 한 버전 — **불변**.
///
/// Design Ref: §3.2 · §10 — 수정은 편집이 아니라 새 행이다.
///
/// **왜 불변인가.** 내역(<see cref="LlmCall"/>)이 `PromptVersionId` 를 가리킨다.
/// 본문을 나중에 고칠 수 있으면 "이 결과가 어느 프롬프트에서 나왔나" 라는 질문의 답이
/// 시간에 따라 달라진다. 재현성이 무너지는 지점이 정확히 여기다.
///
/// 활성 버전은 단계마다 하나이고, DB 의 필터 유니크 인덱스가 그것을 강제한다 (§3.5) —
/// 애플리케이션 규칙으로만 두면 동시 활성화에서 둘이 켜진다.
/// </summary>
public sealed class PromptVersion
{
    private PromptVersion()
    {
        // EF Core 재구성용
    }

    private PromptVersion(
        Guid id,
        LlmOperationKind kind,
        AssetCategory? category,
        int version,
        string system,
        string user,
        string jsonSchema,
        string? note,
        DateTimeOffset now)
    {
        Id = id;
        Kind = kind;
        Category = category;
        Version = version;
        System = system;
        User = user;
        JsonSchema = jsonSchema;
        Note = note;
        IsActive = false;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>어느 단계의 프롬프트인가. `Noxtend.Domain` 을 참조하는 유일한 이유다.</summary>
    public LlmOperationKind Kind { get; private set; }

    /// <summary>
    /// 어느 제작 카테고리의 프롬프트인가. <c>null</c> 은 카테고리를 가리지 않는 기본이다.
    ///
    /// 없음이 기본이라는 것이 하위호환의 근거다 — 기존 행이 전부 null 로 남아
    /// 카테고리를 모르는 채 배포돼 있던 파이프라인과 같은 결과를 낸다. Design §4.1
    /// </summary>
    public AssetCategory? Category { get; private set; }

    /// <summary>
    /// <c>(Kind, Category)</c> 안에서 1부터. 전역 일련번호가 아니다 — 카테고리마다
    /// 새로 센다. Design §4.3
    /// </summary>
    public int Version { get; private set; }

    public string System { get; private set; } = string.Empty;
    public string User { get; private set; } = string.Empty;
    public string JsonSchema { get; private set; } = string.Empty;

    /// <summary>"가림 관계 지시를 명시적으로" 처럼 무엇을 바꿨는지. 비교 화면이 읽는다.</summary>
    public string? Note { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// 새 버전. **항상 비활성으로 태어난다** (FR-09).
    ///
    /// 저장이 곧 반영이면 편집 중 실수가 즉시 운영에 나간다. 활성화는 별도 행위다.
    /// </summary>
    public static PromptVersion Create(
        LlmOperationKind kind,
        AssetCategory? category,
        int version,
        string system,
        string user,
        string jsonSchema,
        string? note,
        DateTimeOffset now)
        => new(Guid.NewGuid(), kind, category, version, system, user, jsonSchema, note, now);

    /// <summary>
    /// 활성화. 같은 단계의 이전 활성 버전을 내리는 것은 **호출자의 책임**이다 —
    /// 두 행을 한 트랜잭션에서 바꿔야 하므로 엔티티 혼자서는 보장할 수 없다.
    /// </summary>
    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
