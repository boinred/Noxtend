using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Application.Job;
using Noxtend.Application.Mesh;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 작업 접수.
///
/// Design Ref: §2.2 파이프라인 실행 · §4.2 #5
///
/// 순서가 계약이다. **DB 저장이 먼저이고 큐 적재가 나중이다.** 반대로 하면 워커가
/// 아직 없는 작업을 꺼내게 된다. 그리고 적재가 실패해도 작업은 남는다 —
/// 스위퍼가 재적재하므로 Redis 장애는 유실이 아니라 지연이다 (§6 · R-10).
/// </summary>
public sealed class StartJobHandler(
    IJobRepository jobs,
    IStoredImageRepository images,
    IProviderConfigRepository providers,
    IModelCatalog catalog,
    IPromptCatalog prompts,
    JobOrchestrator orchestrator,
    MeshSelectionValidator meshSelection,
    IClock clock)
{
    public async Task<Result<PipelineJob>> HandleAsync(
        AssetCategory category,
        Guid uploadId,
        Guid providerConfigId,
        string model,
        CancellationToken ct,
        Guid? imageProviderConfigId = null,
        string? imageModel = null,
        Guid? meshProviderConfigId = null,
        string? meshModel = null,
        // 캐릭터 고유 입력 (character-studio §3.2) — 기존 positional 호출부 하위호환
        Gender? gender = null,
        IReadOnlyList<PartHint>? partHints = null,
        // 검수 게이트 opt-in (review-gate §목표) — 전 카테고리 공통, 기본 false 라
        // 기존 접수 호출부(§제외범위 소급 없음)는 안 바뀐다
        bool requiresReview = false)
    {
        if (await images.GetAsync(uploadId, ct) is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobUploadNotFound, "업로드를 찾을 수 없습니다");
        }

        var provider = await providers.GetAsync(providerConfigId, ct);
        if (provider is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        if (!provider.IsEnabled)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobProviderDisabled, "사용 중지된 공급자입니다");
        }

        var modelError = await ValidateModelAsync(providerConfigId, model, ct);
        if (modelError is not null)
        {
            return modelError.Value;
        }

        // 이미지 공급자 검증 (사이클 #7 §4.2 #1). 텍스트와 같은 이유로 접수 시점에 본다 —
        // 분해가 끝난 뒤에야 알면 사용자는 텍스트 세 단계를 치르고 나서 실패를 본다
        var imageError = await ValidateImageProviderAsync(imageProviderConfigId, imageModel, ct);
        if (imageError is not null)
        {
            return imageError.Value;
        }

        // 3D 공급자 검증 (사이클 #10 §10.2). 같은 이유로 접수 시점에 본다
        var meshError = await ValidateMeshProviderAsync(
            meshProviderConfigId, meshModel, imageProviderConfigId, ct);
        if (meshError is not null)
        {
            return meshError.Value;
        }

        // 캐릭터 고유 입력 검증 (character-studio §3.2) — 카테고리 분기는 이 한 곳에만 둔다.
        // 백본·스테이지·오케스트레이터로 새지 않는다 (NFR-04)
        var characterError = ValidateCharacterInput(category, gender, partHints);
        if (characterError is not null)
        {
            return characterError.Value;
        }

        // 활성 프롬프트가 없으면 워커까지 보내지 않는다 (§6). 큐에 넣고 실패시키면
        // 사용자는 "시작됐다" 를 보고 나서 실패를 본다
        if (await FindMissingPromptAsync(category, imageProviderConfigId is not null, ct) is { } missing)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.PromptNotActive, $"{missing} 단계에 활성 프롬프트가 없습니다");
        }

        // 접수가 힌트를 JSON 으로 굳힌다 — 스테이지가 역직렬화·렌더한다 (§4.3)
        var job = PipelineJob.Create(
            category, uploadId, clock.Now,
            imageProviderConfigId, imageModel?.Trim(),
            meshProviderConfigId, meshModel?.Trim(),
            gender, PartHintCodec.Serialize(partHints), requiresReview);
        PlanStages(job, providerConfigId, model.Trim());

        await jobs.AddAsync(job, ct);
        await jobs.SaveChangesAsync(ct);

        await orchestrator.StartAsync(job, ct);

        return Result<PipelineJob>.Ok(job);
    }

    /// <summary>
    /// 공정 계획 — 장면 → 추출 → 분해.
    ///
    /// Design Ref: §2.2 · Plan FR-00
    ///
    /// **선형 의존이다.** 각 공정이 직전 공정을 가리키므로 오케스트레이터가 앞이 끝날
    /// 때마다 다음을 적재한다. 넷째 단계는 여기에 행 하나가 는다 — §2.3 이 주장한 그대로다.
    ///
    /// 셋이 같은 모델을 쓴다 (Plan D-11). `Task.Model` 이 공정마다 있으므로 단계별로
    /// 다른 모델을 쓰는 것은 구조상 가능하나, 이번 비교 변수는 (프롬프트 버전 × 모델)
    /// 쌍으로 충분해 화면을 복잡하게 하지 않는다.
    /// </summary>
    private static void PlanStages(PipelineJob job, Guid providerConfigId, string model)
    {
        Guid? previous = null;
        var ordinal = 0;

        foreach (var kind in StageOrder)
        {
            var task = job.PlanTask(
                kind,
                ordinal++,
                dependsOnTaskId: previous,
                providerConfigId: providerConfigId,
                model: model);

            previous = task.Id;
        }
    }

    /// <summary>실행 순서. 여기 값의 순서가 곧 파이프라인이다.</summary>
    private static readonly TaskKind[] StageOrder =
        [TaskKind.Analyze, TaskKind.Extract, TaskKind.Decompose];

    // 힌트 개수 범위 — 도메인 배치 규칙과 같은 1~20 (character-studio §D-02, PipelineJob.MaxPlacements)
    private const int MinPartHintCount = 1;
    private const int MaxPartHintCount = 20;

    /// <summary>
    /// 캐릭터 고유 입력 검증. 통과하면 <c>null</c>.
    ///
    /// 성별은 캐릭터에서만 필수다(§D-01). 힌트 개수는 도메인 배치 규칙과 같은 범위로 막는다 —
    /// 접수에서 걸러야 잘못된 값이 프롬프트 변수로 흘러 생성에서야 이상해지는 것을 막는다.
    /// </summary>
    private static Result<PipelineJob>? ValidateCharacterInput(
        AssetCategory category, Gender? gender, IReadOnlyList<PartHint>? partHints)
    {
        if (category == AssetCategory.Character && gender is null)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobGenderRequired, "캐릭터 작업은 성별이 필요합니다");
        }

        foreach (var hint in partHints ?? [])
        {
            // 종류는 프롬프트 변수로 렌더되므로 비어 있으면 " 3" 같은 조각이 나가고, 템플릿
            // 구문({{ }})이 들어오면 PromptTemplate.Render 재스캔이 잔여 변수로 오인해 예외를
            // 던진다 — 캐릭터 작업이 통째로 실패한다. 접수에서 걸러 그 실패를 앞당겨 막는다
            if (string.IsNullOrWhiteSpace(hint.Type) || HasTemplateSyntax(hint.Type) || HasTemplateSyntax(hint.Variant))
            {
                return Result<PipelineJob>.Fail(
                    ErrorCode.JobPartHintInvalid, "파츠 힌트 종류·변형이 올바르지 않습니다");
            }

            if (hint.Count is < MinPartHintCount or > MaxPartHintCount)
            {
                return Result<PipelineJob>.Fail(
                    ErrorCode.JobPartHintInvalid,
                    $"'{hint.Type}' 힌트 개수는 {MinPartHintCount}~{MaxPartHintCount} 이어야 합니다 (받음: {hint.Count})");
            }
        }

        return null;
    }

    // 템플릿 치환 구문 포함 여부 — 힌트 값에 {{ 나 }} 가 있으면 렌더 재스캔이 깨진다
    private static bool HasTemplateSyntax(string? value)
        => value is not null && (value.Contains("{{") || value.Contains("}}"));

    /// <summary>
    /// 활성 프롬프트가 없는 첫 단계. 전부 있으면 <c>null</c>.
    ///
    /// 생성 단계는 이미지 공급자를 고른 작업만 검사한다 — 안 고른 작업은 그 단계를
    /// 돌지 않으므로 프롬프트가 없어도 무해하다.
    /// </summary>
    private async Task<TaskKind?> FindMissingPromptAsync(
        AssetCategory category, bool includeGenerate, CancellationToken ct)
    {
        var stages = includeGenerate ? [.. StageOrder, TaskKind.Generate] : StageOrder;

        foreach (var kind in stages)
        {
            // 폴백을 적용한 결과로 판정한다 — 전용이 없어도 기본이 있으면 통과 (Design §6.3)
            if (await prompts.GetActiveAsync(LlmOperation.FromTask(kind), category, ct) is null)
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>
    /// 모델이 그 공급자의 목록에 있는지 확인한다. 통과하면 <c>null</c>.
    ///
    /// **왜 접수 시점에 보는가.** 오래된 탭이 사라진 모델 id 를 보낼 수 있다. 여기서 막지
    /// 않으면 작업이 생성되고 큐에 들어간 뒤 워커가 집어서야 실패하는데, 사용자에게는
    /// "실행했더니 알 수 없는 이유로 실패" 로 보인다.
    ///
    /// 목록은 캐시된다 (§3.2). 사용자가 방금 드롭다운에서 골랐다면 캐시가 살아 있다.
    /// **감수하는 것**: 캐시가 비었고 동시에 공급자 목록 API 가 죽으면 접수가 막힌다.
    /// 목록과 추론은 다른 엔드포인트라 이론적으로 독립이지만, 그 조합은 드물고
    /// 그때 나오는 오류가 워커까지 갔다 오는 오류보다 낫다.
    /// </summary>
    private async Task<Result<PipelineJob>?> ValidateModelAsync(
        Guid providerConfigId,
        string model,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobModelUnavailable, "모델을 선택해야 합니다");
        }

        IReadOnlyList<ProviderModel> available;
        try
        {
            available = await catalog.ListAsync(providerConfigId, ct);
        }
        catch (ProviderCallFailedException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }

        var requested = model.Trim();
        if (!available.Any(m => m.Id == requested))
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobModelUnavailable,
                "선택한 모델을 쓸 수 없습니다. 목록을 새로 불러오세요");
        }

        return null;
    }

    /// <summary>
    /// 이미지 공급자·모델 검증 (사이클 #7 §4.2 #1). 통과하면 <c>null</c>.
    ///
    /// **둘 다 없으면 통과한다** — 이미지 생성 없이 앞 세 단계만 도는 작업을 허용한다.
    /// 한쪽만 있으면 거절이다: 공급자만 있고 모델이 없으면 무엇으로 그릴지 알 수 없다.
    ///
    /// 목록을 텍스트와 따로 묻는다 (§4.2 #7) — 한 목록에 섞으면 텍스트 모델로 그리려는
    /// 요청이 여기를 통과해 워커에서야 실패한다.
    /// </summary>
    private async Task<Result<PipelineJob>?> ValidateImageProviderAsync(
        Guid? imageProviderConfigId,
        string? imageModel,
        CancellationToken ct)
    {
        if (imageProviderConfigId is not { } configId)
        {
            return null;
        }

        var provider = await providers.GetAsync(configId, ct);
        if (provider is null)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobImageProviderNotFound, "이미지 공급자를 찾을 수 없습니다");
        }

        if (!provider.IsEnabled)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobImageProviderDisabled, "사용 중지된 이미지 공급자입니다");
        }

        if (string.IsNullOrWhiteSpace(imageModel))
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobImageModelUnavailable, "이미지 모델을 선택해야 합니다");
        }

        IReadOnlyList<ProviderModel> available;
        try
        {
            available = await catalog.ListImageModelsAsync(configId, ct);
        }
        catch (ProviderCallFailedException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }

        if (!available.Any(m => m.Id == imageModel.Trim()))
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobImageModelUnavailable,
                "선택한 이미지 모델을 쓸 수 없습니다. 목록을 새로 불러오세요");
        }

        return null;
    }

    /// <summary>
    /// 3D 공급자 검증 (§10.2).
    ///
    /// **한쪽만 오는 것을 막는 것이 절반이다.** 공급자만 고르고 모델을 못 불러온 화면이
    /// 그대로 접수를 보내면, 작업은 3D 를 만들 것처럼 보이면서 아무것도 만들지 않는다.
    /// </summary>
    private async Task<Result<PipelineJob>?> ValidateMeshProviderAsync(
        Guid? meshProviderConfigId,
        string? meshModel,
        Guid? imageProviderConfigId,
        CancellationToken ct)
    {
        var hasProvider = meshProviderConfigId is not null;
        var hasModel = !string.IsNullOrWhiteSpace(meshModel);

        // 둘 다 없으면 이미지까지만 도는 정상 작업이다 (NFR-06)
        if (!hasProvider && !hasModel)
        {
            return null;
        }

        if (hasProvider != hasModel)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobMeshModelUnavailable, "3D 공급자와 모델을 함께 선택해야 합니다");
        }

        // **3D 의 입력이 네 방향 이미지다.** 이미지 없이 받아 두면 앞 세 단계를 다 치르고
        // 나서 아무 일도 일어나지 않는다
        if (imageProviderConfigId is null)
        {
            return Result<PipelineJob>.Fail(
                ErrorCode.JobMeshRequiresImages, "3D 를 만들려면 이미지 생성을 함께 선택해야 합니다");
        }

        // 공급자·모델 확인은 공용 검증이 한다 (D-11) — 뒤늦은 지정과 같은 규칙이어야
        // 한쪽에서만 막히는 조합이 생기지 않는다
        return await meshSelection.ValidateAsync(meshProviderConfigId!.Value, meshModel, ct) is { } error
            ? Result<PipelineJob>.Fail(error.Code, error.Message)
            : null;
    }
}
