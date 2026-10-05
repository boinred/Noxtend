using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tuning.Domain.Ports;

/// <summary>
/// Design Ref: §3.2 · §9.1 — 튜닝 저장소 Port.
///
/// 파이프라인 저장소와 같은 규칙이다: 인터페이스는 도메인에, 구현은 Infrastructure 에.
/// `SaveChangesAsync` 를 노출하는 것도 같다 — 여러 엔티티를 한 트랜잭션으로 묶어야
/// 하는 경우(활성 전환)가 있으므로 커밋 시점을 호출자가 정한다.
/// </summary>
public interface IPromptVersionRepository
{
    Task<PromptVersion?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// <c>(Kind, Category)</c> 의 활성 버전 — 정확 일치만. 없으면 null.
    /// 폴백(전용 없으면 기본)은 어댑터가 한다 (Design §3.1) — 리포지토리는 규칙을 모른다.
    /// </summary>
    Task<PromptVersion?> GetActiveAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct);

    /// <summary><c>(Kind, Category)</c> 의 전체 이력 — 정확 일치. 최신 버전이 앞이다.</summary>
    Task<IReadOnlyList<PromptVersion>> ListAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct);

    /// <summary>모든 단계의 활성 버전. 목록 화면이 읽는다.</summary>
    Task<IReadOnlyList<PromptVersion>> ListActiveAsync(CancellationToken ct);

    /// <summary>다음 버전 번호. <c>(Kind, Category)</c> 안에서 1부터 증가한다 (Design §4.3).</summary>
    Task<int> NextVersionAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct);

    Task AddAsync(PromptVersion version, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface ILlmCallRepository
{
    Task AddAsync(LlmCall call, CancellationToken ct);

    /// <summary>작업의 호출 이력. 공정 순서대로.</summary>
    Task<IReadOnlyList<LlmCall>> ListByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// 행 수와 대략적인 크기. Design §2.3-8 — 보존 정책을 만들지 않는 대신
    /// **커지는 것을 알아챌 수 있게** 한다.
    /// </summary>
    Task<LlmCallStats> GetStatsAsync(CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// 단가표. 행이 적고 모든 호출 비용 계산에 필요해 통째로 읽는다 —
/// 모델 매칭이 문자열 규칙이라 SQL 로 좁힐 수 없다 (<see cref="ModelPriceBook"/>).
/// </summary>
public interface IModelPriceRepository
{
    Task<ModelPrice?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>모델명·시행일 순. 화면과 비용 계산이 함께 쓴다.</summary>
    Task<IReadOnlyList<ModelPrice>> ListAsync(CancellationToken ct);

    /// <summary>비용 계산용 단가표 한 벌.</summary>
    Task<ModelPriceBook> GetBookAsync(CancellationToken ct);

    /// <summary>같은 모델·같은 시행일 행이 이미 있는가 — 중복은 어느 쪽이 이길지 알 수 없다.</summary>
    Task<bool> ExistsAsync(string model, DateTimeOffset effectiveFrom, Guid? excludingId, CancellationToken ct);

    Task AddAsync(ModelPrice price, CancellationToken ct);
    void Remove(ModelPrice price);
    Task SaveChangesAsync(CancellationToken ct);
}

/// <summary>
/// <paramref name="UnpricedCalls"/> — 단가를 모르는 모델의 호출 수.
/// 0 이 아니면 <paramref name="TotalCostUsd"/> 는 실제보다 낮다.
/// </summary>
public sealed record LlmCallStats(
    long Count, long ApproximateBytes, decimal TotalCostUsd, long UnpricedCalls);

public interface IGoldenSampleRepository
{
    Task<GoldenSample?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<GoldenSample>> ListAsync(CancellationToken ct);
    Task AddAsync(GoldenSample sample, CancellationToken ct);
    Task RemoveAsync(GoldenSample sample, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}

public interface IVerdictRepository
{
    /// <summary>작업 하나에 판정 하나. 없으면 null.</summary>
    Task<Verdict?> GetByJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>여러 작업의 판정을 한 번에. 실행 목록 화면이 N+1 을 피하려고 쓴다.</summary>
    Task<IReadOnlyDictionary<Guid, Verdict>> GetByJobsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct);

    Task AddAsync(Verdict verdict, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
