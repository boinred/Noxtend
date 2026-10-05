using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Ports;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Infrastructure.Persistence.InMemory;

/// <summary>
/// 인프라 없는 튜닝 저장소.
///
/// Design Ref: §8.1 — 파이프라인 저장소와 같은 이유다. 내역·프롬프트 규칙을
/// DB 없이 검증할 수 있어야 회귀가 빠르게 잡힌다.
/// </summary>
public sealed class InMemoryPromptVersionRepository : IPromptVersionRepository
{
    private readonly List<PromptVersion> _versions = [];

    public Task<PromptVersion?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_versions.FirstOrDefault(v => v.Id == id));

    // 정확 일치만 — 폴백은 어댑터가 한다 (Design §5.3)
    public Task<PromptVersion?> GetActiveAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => Task.FromResult(
            _versions.FirstOrDefault(v => v.Kind == kind && v.Category == category && v.IsActive));

    public Task<IReadOnlyList<PromptVersion>> ListAsync(
        LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PromptVersion>>(
            [.. _versions.Where(v => v.Kind == kind && v.Category == category)
                .OrderByDescending(v => v.Version)]);

    // 정렬을 Kind 다음 Category 로 확장한다 (Design §5.3) — EF 구현과 순서를 맞춘다
    public Task<IReadOnlyList<PromptVersion>> ListActiveAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PromptVersion>>(
            [.. _versions.Where(v => v.IsActive).OrderBy(v => v.Kind).ThenBy(v => v.Category)]);

    // 채번은 (Kind, Category) 스코프 — 카테고리마다 1부터 (Design §4.3)
    public Task<int> NextVersionAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => Task.FromResult(
            _versions.Where(v => v.Kind == kind && v.Category == category)
                .Select(v => v.Version).DefaultIfEmpty(0).Max() + 1);

    public Task AddAsync(PromptVersion version, CancellationToken ct)
    {
        _versions.Add(version);
        return Task.CompletedTask;
    }

    /// <summary>
    /// DB 의 필터 유니크 인덱스를 흉내낸다.
    ///
    /// **이것이 없으면 "활성은 하나" 규칙이 인메모리에서만 조용히 깨진다.** 실제 DB 는
    /// 두 번째 활성을 거부하는데 테스트는 통과하는 상황이 생긴다 — 가장 나쁜 종류의
    /// Fake 다 (사이클 #4 의 EF `ValueGeneratedNever` 교훈과 같은 계열).
    /// </summary>
    public Task SaveChangesAsync(CancellationToken ct)
    {
        // 유일성은 {Kind, Category} — DB 의 필터 유니크와 같은 스코프 (Design §4.2).
        // Kind 로만 검사하면 격리 테스트(slice 4)가 구현이 옳아도 예외로 실패한다
        var conflict = _versions
            .Where(v => v.IsActive)
            .GroupBy(v => new { v.Kind, v.Category })
            .FirstOrDefault(g => g.Count() > 1);

        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"{conflict.Key.Kind}/{conflict.Key.Category?.ToString() ?? "기본"} 에 활성 버전이 " +
                $"{conflict.Count()}개입니다 — DB 유니크 인덱스가 거부합니다");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// 단가표의 메모리 구현.
///
/// <see cref="SaveChangesAsync"/> 가 DB 의 유니크 인덱스를 흉내낸다 — 그러지 않으면
/// 테스트에서만 통과하고 실제 DB 에서 깨지는 코드가 나온다.
/// </summary>
public sealed class InMemoryModelPriceRepository : IModelPriceRepository
{
    private readonly List<ModelPrice> _prices = [];

    public Task<ModelPrice?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_prices.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<ModelPrice>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<ModelPrice>>(
            [.. _prices.OrderBy(p => p.Model).ThenByDescending(p => p.EffectiveFrom)]);

    public Task<ModelPriceBook> GetBookAsync(CancellationToken ct)
        => Task.FromResult(new ModelPriceBook(_prices));

    public Task<bool> ExistsAsync(
        string model, DateTimeOffset effectiveFrom, Guid? excludingId, CancellationToken ct)
        => Task.FromResult(_prices.Any(
            p => p.Model == model && p.EffectiveFrom == effectiveFrom && p.Id != excludingId));

    public Task AddAsync(ModelPrice price, CancellationToken ct)
    {
        _prices.Add(price);
        return Task.CompletedTask;
    }

    public void Remove(ModelPrice price) => _prices.Remove(price);

    public Task SaveChangesAsync(CancellationToken ct)
    {
        var duplicate = _prices
            .GroupBy(p => (p.Model, p.EffectiveFrom))
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"단가 중복: {duplicate.Key.Model} @ {duplicate.Key.EffectiveFrom:yyyy-MM-dd}");
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryLlmCallRepository : ILlmCallRepository
{
    private readonly List<LlmCall> _calls = [];

    /// <summary>설정하면 저장이 실패한다 — G-4(기록 실패가 공정을 안 망침) 재현용.</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<LlmCall> All => _calls;

    public Task AddAsync(LlmCall call, CancellationToken ct)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        _calls.Add(call);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LlmCall>> ListByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<LlmCall>>(
            [.. _calls.Where(c => c.JobId == jobId).OrderBy(c => c.At)]);

    /// <summary>단가표를 주입해 두면 비용까지 센다. 비워 두면 전부 미등록으로 잡힌다.</summary>
    public ModelPriceBook Prices { get; set; } = ModelPriceBook.Empty;

    public Task<LlmCallStats> GetStatsAsync(CancellationToken ct)
    {
        var costs = _calls
            .Select(c => Prices.Estimate(c.Model, c.At, c.InputTokens, c.OutputTokens, c.OutputImages))
            .ToList();

        return Task.FromResult(new LlmCallStats(
            _calls.Count,
            _calls.Sum(c => (long)c.RequestPayload.Length + (c.ResponsePayload?.Length ?? 0)) * 2,
            costs.Where(c => c is not null).Sum(c => c!.Value),
            costs.Count(c => c is null)));
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class InMemoryGoldenSampleRepository : IGoldenSampleRepository
{
    private readonly List<GoldenSample> _samples = [];

    public Task<GoldenSample?> GetAsync(Guid id, CancellationToken ct)
        => Task.FromResult(_samples.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<GoldenSample>> ListAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<GoldenSample>>(
            [.. _samples.OrderByDescending(s => s.CreatedAt)]);

    public Task AddAsync(GoldenSample sample, CancellationToken ct)
    {
        _samples.Add(sample);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(GoldenSample sample, CancellationToken ct)
    {
        _samples.Remove(sample);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class InMemoryVerdictRepository : IVerdictRepository
{
    private readonly List<Verdict> _verdicts = [];

    public Task<Verdict?> GetByJobAsync(Guid jobId, CancellationToken ct)
        => Task.FromResult(_verdicts.FirstOrDefault(v => v.JobId == jobId));

    public Task<IReadOnlyDictionary<Guid, Verdict>> GetByJobsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<Guid, Verdict>>(
            _verdicts.Where(v => jobIds.Contains(v.JobId)).ToDictionary(v => v.JobId));

    public Task AddAsync(Verdict verdict, CancellationToken ct)
    {
        _verdicts.Add(verdict);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
}
