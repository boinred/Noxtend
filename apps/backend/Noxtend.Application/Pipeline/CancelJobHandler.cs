using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 작업 취소.
///
/// Design Ref: §2.2 취소 · §4.2 #8
///
/// **워커를 기다리지 않고 즉시 반환한다.** 사용자가 취소를 눌렀는데 응답이 10분 뒤에
/// 오면 그것은 취소가 아니다. 실행 중이던 공정은 다음 리스 갱신(≤15초)에 상태를 보고
/// 스스로 끊는다 — 중단은 협조적이며 브로커는 워커 프로세스 안을 모른다.
///
/// 큐에서 메시지를 지우지 않는다(툼스톤). Streams·RabbitMQ 모두 임의 메시지 삭제는
/// 어렵거나 비싸다. 워커가 꺼낼 때 상태를 확인하고 처리 없이 Ack 한다.
/// </summary>
public sealed class CancelJobHandler(IJobRepository jobs, IClock clock)
{
    public async Task<Result<PipelineJob>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
        {
            return Result<PipelineJob>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        if (job.IsTerminal)
        {
            // 이미 끝난 작업의 취소는 409 다 — 화면이 결과를 보고 있는데 취소가 되면 혼란스럽다
            return Result<PipelineJob>.Fail(
                ErrorCode.JobAlreadyTerminal,
                job.Status.ToString().ToLowerInvariant());
        }

        job.Cancel(clock.Now);
        await jobs.SaveChangesAsync(ct);

        return Result<PipelineJob>.Ok(job);
    }
}
