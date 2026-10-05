using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 검수 서술 확인 단계 (review-gate-staged 사이클 1) — 확정·되돌리기·서술 편집·팔레트 편집.
///
/// 네 동작이 적재·예외 매핑·저장을 공유해 한 클래스에 둔다. 도메인 규칙은 전부
/// <see cref="PipelineJob"/> 에 있고, 여기는 예외를 <see cref="ErrorCode"/> 로 접기만 한다.
/// </summary>
public sealed class ReviewDescriptionsHandler(IJobRepository jobs, JobOrchestrator orchestrator, IClock clock)
{
    // 서술 확정 — 팬아웃을 큐에 적재
    public async Task<Result<PipelineJob>> ConfirmAsync(Guid jobId, CancellationToken ct)
    {
        var result = await MutateAsync(jobId, job => job.ConfirmDescriptions(clock.Now), ct);
        if (result.IsSuccess)
        {
            await orchestrator.StartAsync(result.Value!, ct);
        }

        return result;
    }

    public Task<Result<PipelineJob>> ReturnToBoxesAsync(Guid jobId, CancellationToken ct)
        => MutateAsync(jobId, job => job.ReturnToBoxes(), ct);

    public Task<Result<PipelineJob>> EditDescriptionAsync(
        Guid jobId, Guid partId, string description, CancellationToken ct)
        => MutateAsync(jobId, job => job.EditReviewDescription(partId, description), ct);

    public Task<Result<PipelineJob>> EditPaletteAsync(
        Guid jobId, IReadOnlyList<PaletteEntry> palette, CancellationToken ct)
        => MutateAsync(jobId, job => job.EditReviewPalette(palette), ct);

    // 적재 → 도메인 동작 → 예외 매핑 → 저장
    private async Task<Result<PipelineJob>> MutateAsync(
        Guid jobId, Action<PipelineJob> mutate, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        try
        {
            mutate(job);
        }
        catch (ReviewPhaseMismatchException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewPhaseMismatch, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.ReviewNotPending, ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Result<PipelineJob>.Fail(ErrorCode.PartNotFound, ex.Message);
        }
        catch (PartValidationException ex)
        {
            var code = ex.Error == PartValidationError.PaletteInvalid
                ? ErrorCode.PaletteInvalid
                : ErrorCode.PartDescriptionEmpty;
            return Result<PipelineJob>.Fail(code, ex.Message);
        }

        await jobs.SaveChangesAsync(ct);
        return Result<PipelineJob>.Ok(job);
    }
}
