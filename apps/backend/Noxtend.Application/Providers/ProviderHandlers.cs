using System.Diagnostics;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Provider;

namespace Noxtend.Application.Providers;

/// <summary>
/// 공급자 등록.
///
/// Design Ref: §2.2 · §8.2 #22
///
/// **평문 키가 저장소에 닿지 않는다.** 여기서 암호화하고 끝 4자리를 뽑은 뒤,
/// 엔티티는 암호문만 받는다 (<see cref="ProviderConfig.Create"/> 가 평문을 받지 않는다).
/// </summary>
public sealed class CreateProviderHandler(
    IProviderConfigRepository configs,
    ISecretProtector protector,
    IClock clock)
{
    public async Task<Result<ProviderConfig>> HandleAsync(
        string displayName,
        ProviderKind kind,
        string apiKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result<ProviderConfig>.Fail(ErrorCode.ProviderKeyRequired, "API 키가 필요합니다");
        }

        var config = ProviderConfig.Create(
            displayName.Trim(),
            kind,
            protector.Protect(apiKey),
            ProviderConfig.ExtractLast4(apiKey),
            clock.Now);

        await configs.AddAsync(config, ct);
        await configs.SaveChangesAsync(ct);

        return Result<ProviderConfig>.Ok(config);
    }
}

/// <summary>
/// 공급자 수정.
///
/// Design Ref: §4.2 #11 · §8.2 #23 — <c>apiKey</c> 가 <c>null</c> 이면 기존 암호문을 유지한다.
/// 화면이 키를 되읽을 수 없으므로 수정마다 재입력을 요구하면 실수로 지워진다.
/// </summary>
public sealed class UpdateProviderHandler(
    IProviderConfigRepository configs,
    ISecretProtector protector,
    IClock clock)
{
    public async Task<Result<ProviderConfig>> HandleAsync(
        Guid id,
        string displayName,
        ProviderKind kind,
        string? apiKey,
        bool isEnabled,
        CancellationToken ct)
    {
        var config = await configs.GetAsync(id, ct);
        if (config is null)
        {
            return Result<ProviderConfig>.Fail(ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        // 빈 문자열도 "바꾸지 않음" 으로 다룬다. 폼이 빈 입력창을 그대로 보내면
        // 사용자는 아무것도 안 했는데 키가 빈 값으로 덮인다
        var hasNewKey = !string.IsNullOrWhiteSpace(apiKey);

        config.Update(
            displayName.Trim(),
            kind,
            hasNewKey ? protector.Protect(apiKey!) : null,
            hasNewKey ? ProviderConfig.ExtractLast4(apiKey!) : null,
            isEnabled,
            clock.Now);

        await configs.SaveChangesAsync(ct);

        return Result<ProviderConfig>.Ok(config);
    }
}

public sealed class DeleteProviderHandler(IProviderConfigRepository configs)
{
    public async Task<Result<bool>> HandleAsync(Guid id, CancellationToken ct)
    {
        var config = await configs.GetAsync(id, ct);
        if (config is null)
        {
            return Result<bool>.Fail(ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        await configs.RemoveAsync(config, ct);
        await configs.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }
}

public sealed class ListProvidersHandler(IProviderConfigRepository configs)
{
    public Task<IReadOnlyList<ProviderConfig>> HandleAsync(CancellationToken ct)
        => configs.ListAsync(ct);
}

/// <summary>
/// 고를 수 있는 모델 목록.
///
/// Design Ref: §4.2 #14 (신설) — 스튜디오의 모델 드롭다운이 이것을 읽는다.
///
/// 실패를 빈 목록으로 바꾸지 않는다. 스튜디오는 "연결 확인부터 하세요" 로 막아야 하고,
/// 그 안내가 맞는지는 "왜 비었나" 에 달렸다.
/// </summary>
public sealed class ListProviderModelsHandler(
    IProviderConfigRepository configs,
    IModelCatalog catalog)
{
    public async Task<Result<IReadOnlyList<ProviderModel>>> HandleAsync(Guid id, CancellationToken ct)
    {
        var config = await configs.GetAsync(id, ct);
        if (config is null)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        try
        {
            var models = await catalog.ListAsync(id, ct);

            if (models.Count == 0)
            {
                return Result<IReadOnlyList<ProviderModel>>.Fail(
                    ErrorCode.ProviderNoVisionModels,
                    "이 공급자에 이미지를 읽을 수 있는 모델이 없습니다");
            }

            return Result<IReadOnlyList<ProviderModel>>.Ok(models);
        }
        catch (ProviderCallFailedException ex)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderCallFailed, ex.GetType().Name);
        }
    }

    /// <summary>
    /// 이미지 생성 모델 목록 (사이클 #7 §4.2 #7).
    ///
    /// **텍스트 목록과 나눈다.** 한 목록에 섞으면 사용자가 텍스트 단계에 이미지 모델을
    /// 고를 수 있고, 그 오류는 접수가 아니라 실행 시점에야 드러난다.
    ///
    /// **빈 목록을 실패로 만들지 않는다.** Anthropic 처럼 이미지 생성이 아예 없는
    /// 공급자가 있고, 그것은 오류가 아니라 사실이다 — 화면은 그 공급자를 고를 수 없게
    /// 하면 된다. 텍스트 쪽이 빈 목록을 실패로 다루는 것은 "이미지를 읽는 모델이 하나도
    /// 없다" 가 키 권한 문제일 수 있어서다.
    /// </summary>
    public async Task<Result<IReadOnlyList<ProviderModel>>> HandleImageModelsAsync(
        Guid id, CancellationToken ct)
    {
        if (await configs.GetAsync(id, ct) is null)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        try
        {
            return Result<IReadOnlyList<ProviderModel>>.Ok(
                await catalog.ListImageModelsAsync(id, ct));
        }
        catch (ProviderCallFailedException ex)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderCallFailed, ex.GetType().Name);
        }
    }

    /// <summary>
    /// 3D 생성 모델 목록 (사이클 #10 §10.1).
    ///
    /// 이미지 쪽과 규칙이 같다 — 빈 목록은 실패가 아니라 "이 공급자로는 못 만든다" 다.
    /// </summary>
    /// <summary>
    /// 3D 공급자의 남은 크레딧. **연결 확인에만 쓴다.**
    ///
    /// 실패를 <c>Result</c> 로 감싸지 않는다 — 못 읽는 것은 오류가 아니라 모른다는
    /// 뜻이고, 그 사실이 <c>null</c> 로 그대로 전달된다.
    /// </summary>
    public async Task<int?> HandleMeshCreditBalanceAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await catalog.GetMeshCreditBalanceAsync(id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 잔액 하나 때문에 연결 확인을 실패로 만들지 않는다
            return null;
        }
    }

    public async Task<Result<IReadOnlyList<ProviderModel>>> HandleMeshModelsAsync(
        Guid id, CancellationToken ct)
    {
        if (await configs.GetAsync(id, ct) is null)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다");
        }

        try
        {
            return Result<IReadOnlyList<ProviderModel>>.Ok(
                await catalog.ListMeshModelsAsync(id, ct));
        }
        catch (ProviderCallFailedException ex)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(ErrorCode.ProviderCallFailed, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<IReadOnlyList<ProviderModel>>.Fail(
                ErrorCode.ProviderCallFailed, ex.GetType().Name);
        }
    }
}

/// <summary>
/// 연결 확인.
///
/// Design Ref: §4.2 #13 — **공급자 원문 오류를 그대로 흘리지 않는다.** 키가 메시지에
/// 섞일 수 있다. 어댑터가 상태 코드만 남기고, 여기서 그것을 코드로 감싼다.
///
/// **확인 방법이 추출 호출에서 모델 목록 조회로 바뀌었다.** 이전에는 1×1 PNG 로
/// 실제 추출을 불러 "닿는가" 를 봤다. 이제 스튜디오가 모델 목록에 의존해 실행되므로
/// (목록이 비면 막힌다) 확인해야 할 것은 바로 그 전제조건이다. 부수 효과로
/// 추론 토큰을 쓰지 않고, 목록 조회는 GET 이라 훨씬 빠르다.
/// </summary>
public sealed class TestProviderHandler(
    IProviderConfigRepository configs,
    ListProviderModelsHandler models)
{
    public async Task<Result<ProviderTestResult>> HandleAsync(Guid id, CancellationToken ct)
    {
        var config = await configs.GetAsync(id, ct);
        if (config is null)
        {
            return Result<ProviderTestResult>.Fail(ErrorCode.ProviderNotFound, "공급자를 찾을 수 없습니다.");
        }

        var stopwatch = Stopwatch.StartNew();
        int? textModelCount = null;
        int? imageModelCount = null;
        int? meshModelCount = null;
        int? meshCreditBalance = null;

        // Capability-specific connection evidence
        if (ProviderCapabilities.Supports(config.Kind, ProviderCapability.TextAnalysis))
        {
            var result = await models.HandleAsync(id, ct);
            if (!result.IsSuccess)
            {
                return Result<ProviderTestResult>.Fail(result.ErrorCode!, result.ErrorMessage!);
            }

            textModelCount = result.Value!.Count;
        }

        // Capability-specific image credential validation
        if (ProviderCapabilities.Supports(config.Kind, ProviderCapability.ImageGeneration))
        {
            var result = await models.HandleImageModelsAsync(id, ct);
            if (!result.IsSuccess)
            {
                return Result<ProviderTestResult>.Fail(result.ErrorCode!, result.ErrorMessage!);
            }

            imageModelCount = result.Value!.Count;
        }

        // 3D 공급자는 잔액 조회로 키를 확인한다 — credit 을 쓰지 않는다 (NFR-08)
        if (ProviderCapabilities.Supports(config.Kind, ProviderCapability.MeshGeneration))
        {
            var result = await models.HandleMeshModelsAsync(id, ct);
            if (!result.IsSuccess)
            {
                return Result<ProviderTestResult>.Fail(result.ErrorCode!, result.ErrorMessage!);
            }

            meshModelCount = result.Value!.Count;

            // **잔액 조회가 실패해도 연결 확인은 성공이다.** 키는 이미 위에서 확인됐고,
            // 잔액은 있으면 좋은 값이다 — 못 읽었다고 "연결 실패" 로 보이면 안 된다
            meshCreditBalance = await models.HandleMeshCreditBalanceAsync(id, ct);
        }

        return Result<ProviderTestResult>.Ok(
            new ProviderTestResult(
                true,
                (int)stopwatch.ElapsedMilliseconds,
                textModelCount,
                imageModelCount,
                meshModelCount,
                meshCreditBalance));
    }
}

/// <summary>Capability-specific model counts as connection evidence.</summary>
public sealed record ProviderTestResult(
    bool Ok,
    int LatencyMs,
    int? TextModelCount,
    int? ImageModelCount,
    int? MeshModelCount,

    /// <summary>
    /// 3D 공급자의 남은 크레딧. **읽지 못했으면 <c>null</c>** — 0 과 다르다.
    ///
    /// 파츠 하나가 크레딧 30 이라 남은 양을 모르면 작업 도중에 멈춘다.
    /// </summary>
    int? MeshCreditBalance);
