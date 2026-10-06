using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Tuning.Domain.Call;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Ports;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Infrastructure.Persistence.Repositories;

/// <summary>Design Ref: §9.1 — 튜닝 Port 의 EF 구현.</summary>
public sealed class EfPromptVersionRepository(NoxtendDbContext db) : IPromptVersionRepository
{
    public Task<PromptVersion?> GetAsync(Guid id, CancellationToken ct)
        => db.PromptVersions.FirstOrDefaultAsync(p => p.Id == id, ct);

    // 정확 일치만 — 폴백은 어댑터가 한다 (Design §5.3).
    // EF Core 관계형 NULL 보정(기본 ON)이 category==null 을 [Category] IS NULL 로 번역한다
    public Task<PromptVersion?> GetActiveAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => db.PromptVersions.FirstOrDefaultAsync(
            p => p.Kind == kind && p.Category == category && p.IsActive, ct);

    public async Task<IReadOnlyList<PromptVersion>> ListAsync(
        LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
        => await db.PromptVersions
            .Where(p => p.Kind == kind && p.Category == category)
            .OrderByDescending(p => p.Version)
            .ToListAsync(ct);

    // 카테고리가 섞여 나오므로 정렬을 Kind 다음 Category 로 확장한다 (Design §5.3) —
    // 같은 kind 안 순서가 미정의면 화면이 비결정적으로 흔들린다
    public async Task<IReadOnlyList<PromptVersion>> ListActiveAsync(CancellationToken ct)
        => await db.PromptVersions
            .Where(p => p.IsActive)
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.Category)
            .ToListAsync(ct);

    /// <summary>
    /// 다음 버전 번호.
    ///
    /// 동시에 두 개를 만들면 같은 번호가 나올 수 있지만, (Kind, Category, Version) 유니크
    /// 인덱스가 두 번째를 거부한다. 관리자 한 명이 쓰는 화면이라 낙관적으로 둔다 — 락을 걸면
    /// 쓰이지 않을 경합 처리 코드가 생긴다.
    /// </summary>
    public async Task<int> NextVersionAsync(LlmOperationKind kind, AssetCategory? category, CancellationToken ct)
    {
        // 채번은 (Kind, Category) 스코프 — 카테고리마다 1부터 (Design §4.3)
        var max = await db.PromptVersions
            .Where(p => p.Kind == kind && p.Category == category)
            .MaxAsync(p => (int?)p.Version, ct);

        return (max ?? 0) + 1;
    }

    public async Task AddAsync(PromptVersion version, CancellationToken ct)
        => await db.PromptVersions.AddAsync(version, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class EfLlmCallRepository(IDbContextFactory<NoxtendDbContext> contexts) : ILlmCallRepository, IDisposable
{
    // 워커 리스 조회의 추적 초기화와 호출 내역 저장의 소유권 분리
    private readonly NoxtendDbContext db = contexts.CreateDbContext();

    public void Dispose() => db.Dispose();
    public async Task AddAsync(LlmCall call, CancellationToken ct)
        => await db.LlmCalls.AddAsync(call, ct);

    public async Task<IReadOnlyList<LlmCall>> ListByJobAsync(Guid jobId, CancellationToken ct)
        => await db.LlmCalls
            .Where(c => c.JobId == jobId)
            .OrderBy(c => c.At)
            .ToListAsync(ct);

    /// <summary>
    /// 규모 파악용 (§2.3-8).
    ///
    /// 크기는 **어림값**이다. 정확한 바이트를 재려면 시스템 뷰를 읽어야 하는데, 알고 싶은
    /// 것은 "지금 문제인가" 뿐이라 자릿수만 맞으면 된다.
    /// </summary>
    public async Task<LlmCallStats> GetStatsAsync(CancellationToken ct)
    {
        var count = await db.LlmCalls.LongCountAsync(ct);

        if (count == 0)
        {
            return new LlmCallStats(0, 0, 0m, 0);
        }

        var payloadLength = await db.LlmCalls
            .SumAsync(c => (long)c.RequestPayload.Length + (c.ResponsePayload!.Length), ct);

        // 비용은 모델별·시행일별 단가가 필요해 SQL 로 못 낸다. 토큰과 시각만 뽑아
        // 메모리에서 계산한다 —
        // 행이 수만 건이 되면 집계 표가 필요하겠지만, 그때 §2.3-8 의 숫자가 알려준다
        var tokens = await db.LlmCalls
            .Select(c => new { c.Model, c.At, c.InputTokens, c.OutputTokens, c.OutputImages })
            .ToListAsync(ct);

        var book = new ModelPriceBook(await db.ModelPrices.ToListAsync(ct));

        decimal total = 0m;
        long unpriced = 0;

        foreach (var call in tokens)
        {
            // 장 수를 빠뜨리면 이미지 호출이 전부 미등록으로 잡혀 합계가 실제보다 낮아진다
            var cost = book.Estimate(
                call.Model, call.At, call.InputTokens, call.OutputTokens, call.OutputImages);
            if (cost is null)
            {
                unpriced++;
            }
            else
            {
                total += cost.Value;
            }
        }

        // NVARCHAR 는 문자당 2바이트
        return new LlmCallStats(count, payloadLength * 2, total, unpriced);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class EfGoldenSampleRepository(NoxtendDbContext db) : IGoldenSampleRepository
{
    public Task<GoldenSample?> GetAsync(Guid id, CancellationToken ct)
        => db.GoldenSamples.FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<IReadOnlyList<GoldenSample>> ListAsync(CancellationToken ct)
        => await db.GoldenSamples.OrderByDescending(g => g.CreatedAt).ToListAsync(ct);

    public async Task AddAsync(GoldenSample sample, CancellationToken ct)
        => await db.GoldenSamples.AddAsync(sample, ct);

    public Task RemoveAsync(GoldenSample sample, CancellationToken ct)
    {
        db.GoldenSamples.Remove(sample);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class EfVerdictRepository(NoxtendDbContext db) : IVerdictRepository
{
    public Task<Verdict?> GetByJobAsync(Guid jobId, CancellationToken ct)
        => db.Verdicts.FirstOrDefaultAsync(v => v.JobId == jobId, ct);

    /// <summary>실행 목록이 판정을 함께 보여준다 — 행마다 조회하면 N+1 이 된다.</summary>
    public async Task<IReadOnlyDictionary<Guid, Verdict>> GetByJobsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct)
    {
        if (jobIds.Count == 0)
        {
            return new Dictionary<Guid, Verdict>();
        }

        var found = await db.Verdicts.Where(v => jobIds.Contains(v.JobId)).ToListAsync(ct);
        return found.ToDictionary(v => v.JobId);
    }

    public async Task AddAsync(Verdict verdict, CancellationToken ct)
        => await db.Verdicts.AddAsync(verdict, ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>단가표의 EF 구현.</summary>
public sealed class EfModelPriceRepository(NoxtendDbContext db) : IModelPriceRepository
{
    public Task<ModelPrice?> GetAsync(Guid id, CancellationToken ct)
        => db.ModelPrices.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<ModelPrice>> ListAsync(CancellationToken ct)
        => await db.ModelPrices
            .OrderBy(p => p.Model)
            .ThenByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);

    public async Task<ModelPriceBook> GetBookAsync(CancellationToken ct)
        => new(await db.ModelPrices.ToListAsync(ct));

    public Task<bool> ExistsAsync(
        string model, DateTimeOffset effectiveFrom, Guid? excludingId, CancellationToken ct)
        => db.ModelPrices.AnyAsync(
            p => p.Model == model && p.EffectiveFrom == effectiveFrom
                 && (excludingId == null || p.Id != excludingId),
            ct);

    public async Task AddAsync(ModelPrice price, CancellationToken ct)
        => await db.ModelPrices.AddAsync(price, ct);

    public void Remove(ModelPrice price) => db.ModelPrices.Remove(price);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
