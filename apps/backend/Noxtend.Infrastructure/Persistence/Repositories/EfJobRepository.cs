using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;
using Noxtend.Domain.Sprites;
using TaskStatus = Noxtend.Domain.Job.TaskStatus;

namespace Noxtend.Infrastructure.Persistence.Repositories;

/// <summary>
/// Design Ref: §3.2 · §9.3
///
/// 종료 상태를 <c>IsTerminal</c> 대신 명시적 목록으로 쓴다. 계산 속성은 SQL 로 번역되지
/// 않아 전체 조회 후 메모리 필터가 되는데, 그러면 "홈 최근 10건" 이 테이블 전체를 읽는다.
/// </summary>
public sealed class EfJobRepository(NoxtendDbContext db) : IJobRepository
{
    /// <remarks>
    /// **<see cref="PipelineJob.IsTerminal"/> 과 같은 뜻이어야 한다.** 저것이 계산 속성이라
    /// 여기에 다시 적을 수밖에 없는데, `PartiallySucceeded` 가 생겼을 때 이쪽만 빠져서
    /// 끝난 작업이 진행 중 목록에 영구히 남고 스위퍼가 매번 집어 들었다.
    /// </remarks>
    private static readonly JobStatus[] TerminalStatuses =
    [
        JobStatus.Succeeded,
        JobStatus.PartiallySucceeded,
        JobStatus.Failed,
        JobStatus.Canceled,
    ];

    public async Task AddAsync(PipelineJob job, CancellationToken ct)
        => await db.Jobs.AddAsync(job, ct);

    /// <summary>
    /// 공정·파츠는 AutoInclude 다 — 작업 상태 판정이 공정 전체를 본다.
    ///
    /// **`AsSplitQuery` 가 없으면 곱이 터진다** (사이클 #9). 컬렉션 셋을 한 쿼리로 조인하면
    /// 행 수가 곱해지는데, 파츠 아래 배치가 붙으면서 넷이 됐다 — 실제 작업 하나에서
    /// `43 공정 × 10 파츠 × 84 배치 × 40 이미지` 로 140만 행이 되어 API 가
    /// `OutOfMemoryException` 으로 죽었다. 컬렉션마다 쿼리를 나누면 합이 된다.
    /// </summary>
    public Task<PipelineJob?> GetAsync(Guid jobId, CancellationToken ct)
        => db.Jobs.AsSplitQuery().FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public Task<PipelineJob?> ReloadAsync(Guid jobId, CancellationToken ct)
    {
        // 병렬 생성 워커의 오래된 aggregate 추적 상태 제거
        db.ChangeTracker.Clear();
        return db.Jobs.AsSplitQuery().FirstOrDefaultAsync(j => j.Id == jobId, ct);
    }

    /// <summary>
    /// 소유 컬렉션이라 작업을 거쳐 들어간다. 인덱스가 있는 것은 `JobId`·`PartId` 이고
    /// 이 조회는 기본키를 쓰므로 작업 전체를 훑지 않는다.
    /// </summary>
    public Task<GeneratedImage?> GetGeneratedImageAsync(Guid imageId, CancellationToken ct)
        => db.Jobs
            .AsNoTracking()
            .SelectMany(j => j.GeneratedImages)
            .FirstOrDefaultAsync(i => i.Id == imageId, ct);

    /// <summary>이미지와 같은 이유로 작업을 거쳐 들어간다 — 소유 컬렉션이다.</summary>
    public Task<GeneratedMesh?> GetGeneratedMeshAsync(Guid meshId, CancellationToken ct)
        => db.Jobs
            .AsNoTracking()
            .SelectMany(j => j.GeneratedMeshes)
            .FirstOrDefaultAsync(mesh => mesh.Id == meshId, ct);

    public Task<SpriteAcceptedRequest?> GetSpriteRequestAsync(Guid requestId, CancellationToken ct)
        => db.Jobs.AsNoTracking().Where(j => j.Sprites != null)
            .SelectMany(j => j.Sprites!.Requests).FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

    public Task<PipelineJob?> GetByTaskAsync(Guid taskId, CancellationToken ct)
        => db.Jobs.AsSplitQuery().FirstOrDefaultAsync(j => j.Tasks.Any(t => t.Id == taskId), ct);

    /// <summary>
    /// **행을 끌어오지 않고 DB 가 센다.** 목록과 같은 조건이어야 두 수가 어긋나지 않는다.
    /// </summary>
    public async Task<int> CountAsync(
        JobListFilter filter,
        AssetCategory? category,
        CancellationToken ct,
        ProductionMode? productionMode = null)
    {
        var query = filter == JobListFilter.Terminal
            ? db.Jobs.Where(j => TerminalStatuses.Contains(j.Status))
            : db.Jobs.Where(j => !TerminalStatuses.Contains(j.Status));

        if (productionMode is { } mode) query = query.Where(j => j.ProductionMode == mode);

        if (category is { } wanted)
        {
            query = query.Where(j => j.Category == wanted);
        }

        return await query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineJob>> ListAsync(
        JobListFilter filter,
        AssetCategory? category,
        int limit,
        CancellationToken ct,
        ProductionMode? productionMode = null)
    {
        var query = filter == JobListFilter.Terminal
            ? db.Jobs.Where(j => TerminalStatuses.Contains(j.Status))
            : db.Jobs.Where(j => !TerminalStatuses.Contains(j.Status));

        if (productionMode is { } mode) query = query.Where(j => j.ProductionMode == mode);

        if (category is { } wanted)
        {
            query = query.Where(j => j.Category == wanted);
        }

        // 목록은 작업이 여럿이라 곱이 더 크다 — 단건 조회보다 먼저 터진다
        return await query
            .AsSplitQuery()
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PipelineJob>> ListBySourceImageAsync(
        Guid sourceImageId,
        CancellationToken ct)
        => await db.Jobs
            .AsSplitQuery()
            .Where(j => j.SourceImageId == sourceImageId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PipelineJob>> ListSweepCandidatesAsync(
        DateTimeOffset now,
        CancellationToken ct)
        => await db.Jobs
            .AsSplitQuery()
            .Where(j => !TerminalStatuses.Contains(j.Status))
            .Where(j =>
                (j.Tasks.Any() && j.Tasks.All(t =>
                    t.Status == TaskStatus.Succeeded ||
                    t.Status == TaskStatus.Failed ||
                    t.Status == TaskStatus.Canceled)) ||
                j.Tasks.Any(t =>
                    t.Status == TaskStatus.Pending ||
                    (t.Status == TaskStatus.Running && t.LeaseExpiresAt != null && t.LeaseExpiresAt <= now)))
            .ToListAsync(ct);

    /// <summary>
    /// 작업과 그에 딸린 모든 것을 지운다.
    ///
    /// **순서가 규칙이다.**
    /// ① 저장소 키를 먼저 모은다 — 행을 지운 뒤에는 어느 파일이 이 작업 것인지 알 수 없다
    /// ② 조건부로 작업을 지운다 — 그 사이 다시 열렸으면 여기서 멈춘다
    /// ③ 딸린 기록(호출·외부 실행)을 같은 트랜잭션에서 지운다
    /// ④ 원본은 **다른 작업이 안 쓸 때만** 지운다
    ///
    /// Blob 삭제는 여기서 하지 않는다. 저장소는 트랜잭션에 못 들어가므로, 기록을 먼저
    /// 확정하고 파일은 호출자가 최선으로 지운다 — 실패하면 고아 파일이 남지만
    /// 사용자에게는 안 보이고 나중에 치울 수 있다.
    ///
    /// **전체를 실행 전략으로 감싼다.** 연결 재시도가 켜져 있으면(`EnableRetryOnFailure`)
    /// 직접 연 트랜잭션은 거절당한다 — 재시도가 트랜잭션 중간부터 다시 시작할 수는 없기
    /// 때문이다. 감싸면 재시도 단위가 삭제 전체가 되어, 끊겼을 때 처음부터 다시 돈다.
    /// </summary>
    public async Task<DeletedJobBlobs?> DeleteIfTerminalAsync(Guid jobId, CancellationToken ct)
        => await db.Database
            .CreateExecutionStrategy()
            .ExecuteAsync(async () => await DeleteInTransactionAsync(jobId, ct));

    private async Task<DeletedJobBlobs?> DeleteInTransactionAsync(Guid jobId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // ① 키 수집
        var imageKeys = await db.Jobs
            .Where(job => job.Id == jobId)
            .SelectMany(job => job.GeneratedImages)
            .Select(image => image.BlobKey)
            .ToListAsync(ct);

        var spriteImageKeys = await db.Jobs
            .Where(job => job.Id == jobId && job.Sprites != null)
            .SelectMany(job => job.Sprites!.Images)
            .Select(image => image.BlobKey)
            .ToListAsync(ct);
        var spriteExportKeys = await db.Jobs
            .Where(job => job.Id == jobId && job.Sprites != null)
            .SelectMany(job => job.Sprites!.Exports)
            .Select(export => export.BlobKey)
            .ToListAsync(ct);
        imageKeys.AddRange(spriteImageKeys);
        imageKeys.AddRange(spriteExportKeys);

        var meshKeys = await db.Jobs
            .Where(job => job.Id == jobId)
            .SelectMany(job => job.GeneratedMeshes)
            .SelectMany(mesh => mesh.Artifacts)
            .Select(artifact => artifact.BlobKey)
            .ToListAsync(ct);

        // **실행에만 남은 결과물도 있다.** 파일을 저장한 뒤 작업에 붙이기 전에 끊기면
        // `MeshRuns` 쪽에만 키가 남는다 — 여기를 안 보면 그 파일이 영영 고아가 된다.
        // 보통은 양쪽이 같은 키라 겹치므로 합치고 중복을 지운다
        var runKeys = await db.MeshRuns
            .Where(run => run.JobId == jobId)
            .SelectMany(run => run.Artifacts)
            .Select(artifact => artifact.BlobKey)
            .ToListAsync(ct);

        meshKeys = [.. meshKeys.Union(runKeys)];

        var sourceImageId = await db.Jobs
            .Where(job => job.Id == jobId)
            .Select(job => job.SourceImageId)
            .FirstOrDefaultAsync(ct);

        // ② 조건부 삭제. 소유 엔티티는 DB cascade 가 함께 지운다
        var removed = await db.Jobs
            .Where(job => job.Id == jobId && PipelineJob.TerminalStatuses.Contains(job.Status))
            .ExecuteDeleteAsync(ct);

        if (removed == 0)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        // ③ 딸린 기록. **비용 원장도 지운다** — 흔적을 남기지 않기로 한 결정이다
        await db.LlmCalls.Where(call => call.JobId == jobId).ExecuteDeleteAsync(ct);
        await db.MeshRuns.Where(run => run.JobId == jobId).ExecuteDeleteAsync(ct);
        // 조립 명세 — 인스턴스 행은 cascade 가 함께 지운다 (scene-assembly FR-09)
        await db.SceneLayouts.Where(layout => layout.JobId == jobId).ExecuteDeleteAsync(ct);

        // 유사도 실행·평가 (background-similarity-tuning §13) — 렌더 blob 키를 먼저 걷고,
        // 평가 행은 run FK cascade 가 함께 지운다. blob 삭제는 commit 뒤 best-effort 다
        var renderKeys = await db.SimilarityEvaluations
            .Where(evaluation => db.SimilarityRuns
                .Where(run => run.JobId == jobId)
                .Select(run => run.Id)
                .Contains(evaluation.RunId))
            .Select(evaluation => evaluation.Render!.BlobKey)
            .Where(key => key != null)
            .ToListAsync(ct);
        imageKeys.AddRange(renderKeys);
        await db.SimilarityRuns.Where(run => run.JobId == jobId).ExecuteDeleteAsync(ct);

        // ④ **원본은 공유될 수 있다.** 같은 업로드로 두 번 접수한 작업이 실제로 있다 —
        // 확인 없이 지우면 남은 작업의 원본이 사라진다
        if (!await db.Jobs.AnyAsync(job => job.SourceImageId == sourceImageId, ct))
        {
            var sourceKey = await db.StoredImages
                .Where(image => image.Id == sourceImageId)
                .Select(image => image.BlobKey)
                .FirstOrDefaultAsync(ct);

            if (sourceKey is not null)
            {
                await db.StoredImages.Where(i => i.Id == sourceImageId).ExecuteDeleteAsync(ct);
                imageKeys.Add(sourceKey);
            }
        }

        await transaction.CommitAsync(ct);

        return new DeletedJobBlobs(imageKeys.Distinct().ToArray(), meshKeys);
    }

    /// <summary>
    /// RowVersion 충돌·유니크 제약 위반을 <see cref="ConcurrencyConflictException"/>으로
    /// 번역한다. Application 계층은 EF Core 를 참조하지 않으므로, 원래 예외 타입 그대로는
    /// 거기서 잡을 수 없다(merge-gate 리뷰 F2).
    ///
    /// **`DbUpdateException` 을 통째로 잡지 않는다.** 그 타입은 FK 위반·NOT NULL 위반·
    /// 길이 초과 같은 진짜 버그도 다 포함한다 — 전부 "동시 충돌"로 번역하면 그런 버그가
    /// 로그 한 줄 없이 "다시 시도하세요"로 삼켜진다(merge-gate 2차 리뷰 B1). SQL Server의
    /// 유니크 제약 위반 번호(2601 중복 인덱스 키, 2627 유니크 제약)만 좁혀서 번역한다 —
    /// 이게 F2 가 막으려던 실제 경합(파츠당 Reconstruct 유니크 제약)의 정확한 신호다.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("다른 요청과 동시에 처리되어 충돌했습니다", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException("다른 요청과 동시에 처리되어 충돌했습니다", ex);
        }
    }
}
