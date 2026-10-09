using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Noxtend.Domain.Common;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Infrastructure.Persistence.Repositories;

public sealed class EfPriceUpdateStore(NoxtendDbContext db, TimeProvider clock) : IPriceUpdateStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task SavePreviewAsync(PriceUpdatePreview preview, CancellationToken ct)
    {
        db.Set<PriceUpdateSnapshot>().Add(new() { Id = preview.Id, ExpiresAt = preview.ExpiresAt, Json = JsonSerializer.Serialize(preview, Json) });
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result<PriceUpdateReceipt>> ApplyAsync(Guid previewId, PriceUpdateApplyInput input, CancellationToken ct)
    {
        if (input.RequestId == Guid.Empty)
            return Failure(ErrorCode.PriceUpdateInvalid, "requestId가 필요합니다");
        var requestJson = JsonSerializer.Serialize(new { PreviewId = previewId,
            CandidateIds = input.CandidateIds?.Order().ToArray(), EffectiveFrom = input.EffectiveFrom?.ToUniversalTime() }, Json);
        try
        {
            // 적용의 자동 재시도 금지와 기존 SQL retry 설정의 트랜잭션 경계 분리
            return await new PriceUpdateExecutionStrategy(db).ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                // 같은 요청·서로 다른 변경안의 적용 직렬화
                await db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'Noxtend.PriceUpdate', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'Price update lock conflict', 1;", ct);
                var saved = await db.Set<PriceUpdateRequestReceipt>().AsNoTracking().SingleOrDefaultAsync(r => r.RequestId == input.RequestId, ct);
                if (saved is not null)
                    return saved.RequestJson == requestJson
                        ? Result<PriceUpdateReceipt>.Ok(JsonSerializer.Deserialize<PriceUpdateReceipt>(saved.ReceiptJson, Json)!)
                        : Failure(ErrorCode.PriceUpdateRequestConflict, "같은 요청 ID의 적용 입력이 다릅니다");
                var snapshot = await db.Set<PriceUpdateSnapshot>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == previewId, ct);
                if (snapshot is null) return Failure(ErrorCode.PriceUpdateNotFound, "변경안을 찾을 수 없습니다");
                var now = clock.GetUtcNow();
                if (now >= snapshot.ExpiresAt) return Failure(ErrorCode.PriceUpdateExpired, "변경안이 만료되었습니다");
                var ids = input.CandidateIds;
                if (ids is null || ids.Count == 0 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
                    return Failure(ErrorCode.PriceUpdateInvalid, "적용 선택이 유효하지 않습니다");
                var preview = JsonSerializer.Deserialize<PriceUpdatePreview>(snapshot.Json, Json)!;
                var selected = preview.Candidates.Where(c => ids.Contains(c.Id)).OrderBy(c => c.Id).ToArray();
                if (selected.Length != ids.Count || selected.Any(c => c.BlockedReason is not null || c.Terms is null || c.ChangeKind is not ("newModel" or "priceChanged")))
                    return Failure(ErrorCode.PriceUpdateInvalid, "적용할 수 없는 후보가 포함되었습니다");
                if (selected.Select(c => c.Model).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
                    return Failure(ErrorCode.PriceUpdateInvalid, "같은 단가 저장 키를 중복 선택했습니다");
                var current = await db.ModelPrices.AsNoTracking().ToListAsync(ct);
                if (selected.Any(c => c.BaselineFingerprint != PriceCandidateComparison.Fingerprint(c.Provider, c.Model, current)))
                    return Failure(ErrorCode.PriceUpdateConflict, "검토 이후 단가가 변경되었습니다");
                var rows = new List<ModelPrice>();
                var items = new List<PriceUpdateReceiptItem>();
                foreach (var candidate in selected)
                {
                    var terms = candidate.Terms!;
                    var lower = terms.OfficialEffectiveFrom is { } official && official > now ? official : now;
                    var effective = input.EffectiveFrom?.ToUniversalTime() ?? lower;
                    if (effective < lower)
                        return Failure(ErrorCode.PriceUpdateInvalid, "시행일이 적용 확정·공식 시행일보다 이릅니다");
                    if (current.Any(p => p.Model.Equals(candidate.Model, StringComparison.OrdinalIgnoreCase) && p.EffectiveFrom == effective))
                        return Failure(ErrorCode.PriceUpdateConflict, "같은 모델·시행 단가가 이미 있습니다");
                    var evidence = JsonSerializer.Serialize(new { candidate.Provider, candidate.Model, candidate.Area,
                        candidate.Operation, candidate.Conditions, terms.OfficialEffectiveFrom, candidate.Evidence }, Json);
                    var price = ModelPrice.CreateCollected(candidate.Model, terms.InputPerMillion, terms.OutputPerMillion,
                        terms.LongContextFrom, terms.LongInputPerMillion, terms.LongOutputPerMillion, effective,
                        "공식 단가 수집 선택 적용", terms.PerImage, candidate.Provider, evidence);
                    rows.Add(price);
                    items.Add(new(candidate.Id, price.Id, effective));
                }
                var receipt = new PriceUpdateReceipt(input.RequestId, previewId, now, items);
                db.ModelPrices.AddRange(rows);
                db.Set<PriceUpdateRequestReceipt>().Add(new() { RequestId = input.RequestId, RequestJson = requestJson, ReceiptJson = JsonSerializer.Serialize(receipt, Json) });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Result<PriceUpdateReceipt>.Ok(receipt);
            });
        }
        catch (ModelPriceInvalidException)
        {
            db.ChangeTracker.Clear();
            return Failure(ErrorCode.PriceUpdateInvalid, "수집 단가의 저장 조건이 유효하지 않습니다");
        }
        catch (Exception ex) when (ex is DbUpdateException { InnerException: SqlException sql } && sql.Number is 2601 or 2627 or 1205
            || ex is SqlException { Number: 1205 or 51000 })
        {
            db.ChangeTracker.Clear();
            return Failure(ErrorCode.PriceUpdateConflict, "다른 단가 적용과 경합했습니다");
        }
    }

    private sealed class PriceUpdateExecutionStrategy(NoxtendDbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }

    private static Result<PriceUpdateReceipt> Failure(string code, string message)
        => Result<PriceUpdateReceipt>.Fail(code, message);
}
