using System.Text.Json;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Prompt;
using Noxtend.Tuning.Domain.Ports;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tuning.Application.Prompts;

/// <summary>
/// [목적] 프롬프트 관리 화면에서 단계별 활성 버전 목록을 가져옵니다.
/// [핵심 동작] 저장소에서 활성 상태인 버전만 읽어 그대로 넘깁니다(카테고리 구분·폴백 없음).
/// [반환] 활성 PromptVersion 목록.
/// </summary>
public sealed class ListActivePromptsHandler(IPromptVersionRepository prompts)
{
    public Task<IReadOnlyList<PromptVersion>> HandleAsync(CancellationToken ct)
        => prompts.ListActiveAsync(ct);
}

/// <summary>
/// [목적] 프롬프트 관리 화면의 그리드 뷰에 쓸 (단계 × 카테고리) 현황을 계산합니다.
/// [핵심 동작] 각 칸마다 전용 프롬프트가 있으면 [전용], 없고 기본이 있으면 [폴백], 둘 다 없으면 [실행불가]로
/// 분류합니다. 폴백(전용 없으면 기본) 규칙은 <see cref="IPromptCatalog"/> 한 곳에만 두고 여기서는 호출만 해
/// 규칙이 두 군데로 나뉘는 것을 막습니다 — 작업 접수 차단(StartJobHandler)과 같은 규칙이라야 화면과 실행이 안 어긋납니다.
/// [반환] 단계마다 각 칸의 상태·버전이 담긴 격자 행 목록(PromptGridRow).
///
/// Design Ref: §12.1 · §3.2(프론트 폴백 재구현 기각).
/// </summary>
public sealed class GetPromptGridHandler(IPromptCatalog catalog, IPromptVersionRepository prompts)
{
    // LLM 을 부르는 슬롯 전부 — Reconstruct 는 LLM 을 부르지 않아 프롬프트 슬롯이 없다.
    // 유사도 평가는 Background 전용이지만 격자에는 행으로 선다 (§15.1)
    private static readonly LlmOperationKind[] PipelineKinds =
        [LlmOperationKind.Analyze, LlmOperationKind.Extract, LlmOperationKind.Decompose,
         LlmOperationKind.RewriteDescriptions, LlmOperationKind.Generate,
         LlmOperationKind.AnalyzeSprites, LlmOperationKind.GenerateSprite, LlmOperationKind.SimilarityEvaluate];

    // 열 순서: 기본(null) → 캐릭터 → 소품 → 배경. 기본 열은 폴백 대상이 없다
    private static readonly AssetCategory?[] Columns =
        [null, AssetCategory.Character, AssetCategory.Object, AssetCategory.Background];

    public async Task<IReadOnlyList<PromptGridRow>> HandleAsync(CancellationToken ct)
    {
        var rows = new List<PromptGridRow>();

        // 행: LLM 을 부르는 단계 전부. 빈 카테고리 행도 나와야 "처음 만들기" 진입점이 격자에서 보인다(§12.2)
        // (유사도 평가 행은 관리자 확장 슬라이스에서 추가한다 — §7.1)
        foreach (var kind in PipelineKinds)
        {
            var columns = new List<PromptGridColumn>();
            foreach (var category in Columns)
            {
                columns.Add(new PromptGridColumn(category, await ResolveCellAsync(kind, category, ct)));
            }

            rows.Add(new PromptGridRow(kind, columns));
        }

        return rows;
    }

    private async Task<PromptGridCell> ResolveCellAsync(
        LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
    {
        // 전용(정확 일치) 활성이 있으면 그 칸은 전용 — 자기 버전을 가리킨다
        var dedicated = await prompts.GetActiveAsync(kind, category, ct);
        if (dedicated is not null)
        {
            return new PromptGridCell(PromptCellStatus.Dedicated, dedicated.Version, dedicated.Id);
        }

        // 기본 슬롯 자체는 폴백이 없다 — 전용이 없으면 실행 불가
        if (category is null)
        {
            return new PromptGridCell(PromptCellStatus.Unavailable, null, null);
        }

        // 전용이 없으니 폴백 판정을 어댑터에 맡긴다(전용→기본). 규칙은 한 곳에만 산다(§3.1·§12.1)
        var effective = await catalog.GetActiveAsync(kind, category.Value, ct);
        return effective is null
            ? new PromptGridCell(PromptCellStatus.Unavailable, null, null)
            : new PromptGridCell(PromptCellStatus.Fallback, effective.Version, effective.VersionId);
    }
}

/// <summary>격자 한 칸의 유효 활성 상태.</summary>
public enum PromptCellStatus
{
    // 전용 활성이 있다
    Dedicated,

    // 전용은 없고 기본으로 폴백해 실행 가능하다
    Fallback,

    // 전용도 기본도 없어 실행 불가
    Unavailable,
}

/// <summary><paramref name="Version"/>·<paramref name="VersionId"/> 는 실행 불가 칸에서 null.</summary>
public sealed record PromptGridCell(PromptCellStatus Status, int? Version, Guid? VersionId);

/// <summary><paramref name="Category"/> 가 null 이면 기본 열.</summary>
public sealed record PromptGridColumn(AssetCategory? Category, PromptGridCell Cell);

public sealed record PromptGridRow(LlmOperationKind Kind, IReadOnlyList<PromptGridColumn> Columns);

/// <summary>
/// [목적] 프롬프트 편집·비교 화면에서 한 단계의 버전 이력을 가져옵니다("어느 버전으로 돌렸나"에도 쓰입니다).
/// [핵심 동작] <c>category</c> 로 슬롯을 정확히 나눠 조회합니다 — null 이면 기본 슬롯, 값이 있으면 그 카테고리 전용.
/// [반환] 그 슬롯의 최신순 버전 목록.
/// </summary>
public sealed class ListPromptVersionsHandler(IPromptVersionRepository prompts)
{
    public Task<IReadOnlyList<PromptVersion>> HandleAsync(
        LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => prompts.ListAsync(kind, category, ct);
}

/// <summary>
/// [목적] 편집 화면에서 프롬프트 새 버전을 만들어 저장합니다.
/// [핵심 동작] 저장 시점에 변수·JSON 스키마를 먼저 검증하고, 채번은 (단계, 카테고리) 스코프로 1부터 매깁니다.
/// 새 버전은 항상 비활성으로 태어납니다 — 켜기는 별도 동작이라, 편집 중 실수가 즉시 운영에 나가지 않습니다.
/// [반환] 성공 시 만들어진 PromptVersion, 변수 오타·잘못된 스키마면 실패 Result.
///
/// Design Ref: §4.2 #18 · FR-08 · §2.3-5(저장 시점 검증) — 잘못된 프롬프트를 켜면 그 단계의 모든 실행이
/// 실패하는데, 그때는 이미 늦기 때문입니다.
/// </summary>
public sealed class CreatePromptVersionHandler(IPromptVersionRepository prompts, IClock clock)
{
    public async Task<Result<PromptVersion>> HandleAsync(
        LlmOperationKind kind,
        AssetCategory? category,
        string system,
        string user,
        string jsonSchema,
        string? note,
        CancellationToken ct)
    {
        // 유사도 평가는 Background 전용 슬롯이다 (background-similarity-tuning §7.1)
        if ((kind is LlmOperationKind.SimilarityEvaluate or LlmOperationKind.AnalyzeSprites or LlmOperationKind.GenerateSprite) && category != AssetCategory.Background)
        {
            return Result<PromptVersion>.Fail(
                ErrorCode.PromptSchemaInvalid, "이 프롬프트는 Background 전용입니다");
        }

        // 두 필드를 모두 본다 — 변수는 어느 쪽에도 쓸 수 있다
        foreach (var body in new[] { system, user })
        {
            if (PromptTemplate.FindUnknownVariable(body, kind) is { } reason)
            {
                return Result<PromptVersion>.Fail(ErrorCode.PromptUnknownVariable, reason);
            }
        }

        if (!IsValidJson(jsonSchema))
        {
            return Result<PromptVersion>.Fail(
                ErrorCode.PromptSchemaInvalid, "jsonSchema 가 유효한 JSON 이 아닙니다");
        }

        // 채번·저장 모두 카테고리 스코프 — null 이면 기본 슬롯
        var active = await prompts.GetActiveAsync(kind, category, ct);
        if (active is not null && active.System == system && active.User == user && active.JsonSchema == jsonSchema)
        {
            return Result<PromptVersion>.Ok(active);
        }

        var version = await prompts.NextVersionAsync(kind, category, ct);
        var created = PromptVersion.Create(kind, category, version, system, user, jsonSchema, note, clock.Now);

        await prompts.AddAsync(created, ct);
        await prompts.SaveChangesAsync(ct);

        return Result<PromptVersion>.Ok(created);
    }

    private static bool IsValidJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// [목적] 편집 화면에서 특정 버전을 켜거나 이전 버전으로 롤백합니다(같은 동작).
/// [핵심 동작] 같은 단계·카테고리의 이전 활성을 먼저 내려 커밋하고, 그다음 대상을 켜 커밋합니다 —
/// 순서를 강제하려고 두 번에 나눕니다. 이미 켜져 있으면 그대로 둡니다(멱등).
/// [반환] 켜진 PromptVersion, 없는 id 면 실패 Result.
///
/// Design Ref: §4.2 #19 · FR-09
///
/// **내리기와 올리기를 두 번에 나눠 커밋한다 — 순서가 강제되어야 하기 때문이다.**
///
/// 한 번에 커밋하면 EF 가 두 UPDATE 를 한 배치로 보내는데, 그 안의 순서를 우리가
/// 정할 수 없다. 실제로 EF 는 **새 버전을 먼저 켰고**, SQL Server 가 문장 단위로
/// 유니크를 검사하므로 그 순간 활성이 둘이 되어 인덱스가 거부했다 —
/// 정당한 전환이 500 으로 떨어졌다.
///
/// 내리기를 먼저 커밋하면 **활성이 0개인 순간**이 생긴다. 그때 접수된 작업은
/// `PROMPT_NOT_ACTIVE` 로 거절된다. 밀리초 단위이고, 무엇보다 **안전한 실패**다 —
/// 잘못된 프롬프트로 작업이 시작되는 것보다 낫다. 관리자가 직접 누르는 동작이라
/// 그 순간에 접수가 겹칠 가능성도 낮다.
/// </summary>
public sealed class ActivatePromptVersionHandler(IPromptVersionRepository prompts)
{
    public async Task<Result<PromptVersion>> HandleAsync(Guid versionId, CancellationToken ct)
    {
        var target = await prompts.GetAsync(versionId, ct);
        if (target is null)
        {
            return Result<PromptVersion>.Fail(
                ErrorCode.PromptVersionNotFound, "프롬프트 버전을 찾을 수 없습니다");
        }

        if (target.IsActive)
        {
            return Result<PromptVersion>.Ok(target); // 멱등
        }

        // 같은 단계·카테고리의 활성만 내린다 (Design §6.1). target.Category 를 넘기지
        // 않으면 캐릭터를 켤 때 기본이 내려가고, 반대로 기본을 켤 때 캐릭터가 내려간다(양방향)
        var current = await prompts.GetActiveAsync(target.Kind, target.Category, ct);
        if (current is not null)
        {
            current.Deactivate();
            await prompts.SaveChangesAsync(ct);
        }

        target.Activate();
        await prompts.SaveChangesAsync(ct);

        return Result<PromptVersion>.Ok(target);
    }
}
