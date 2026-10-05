using Microsoft.EntityFrameworkCore;
using Noxtend.Application.Pipeline;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Api.Workers;

/// <summary>
/// 한 단계(공정 종류)의 워커.
///
/// Design Ref: §2.2 · §10.4 — **단계 추가는 `TaskKind` + 워커 + 스트림 세 가지로
/// 끝나야 한다.** 그래서 이 클래스는 종류를 생성자로 받는다. 분해 단계를 붙일 때
/// 새 클래스를 쓰는 것이 아니라 등록을 하나 더 하면 된다.
///
/// 이 클래스에는 실행 로직이 없다. 큐에서 꺼내 <see cref="ITaskHandler"/> 에 넘기고
/// Ack 하는 것이 전부다 — 리스·취소·결과 반영은 전부 유스케이스에 있고, 그래서 인프라
/// 없이 검증된다 (§8.2 #11~14c).
///
/// **사이클 #7: 핸들러 해석이 매개변수가 됐다.** 이미지 경로가 별도 핸들러를 쓰지만
/// 큐에서 꺼내 넘기는 일은 단계와 무관하다 — 구현 타입을 여기서 직접 해석하면
/// 단계가 늘 때마다 워커 클래스도 늘어난다 (§2.0).
/// </summary>
public sealed class TaskWorker(
    TaskKind kind,
    Func<IServiceProvider, ITaskHandler> resolveHandler,
    IServiceScopeFactory scopeFactory,
    ITaskQueue queue,
    ILogger<TaskWorker> logger) : BackgroundService
{
    /// <summary>어느 단계를 듣는가. 등록 테스트가 이 값으로 커버리지를 확인한다.</summary>
    public TaskKind Kind => kind;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TaskWorker started: {Kind}", kind);

        await foreach (var queued in queue.ConsumeAsync(kind, stoppingToken))
        {
            try
            {
                // 공정마다 새 스코프다. DbContext 를 워커 수명 내내 물고 있으면
                // 변경 추적이 무한히 자라고 한 공정의 실패가 다음 공정을 오염시킨다
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = resolveHandler(scope.ServiceProvider);

                var outcome = await handler.HandleAsync(queued.TaskId, stoppingToken);
                logger.LogInformation("Task {TaskId} -> {Outcome}", queued.TaskId, outcome);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 프로세스 종료. Ack 하지 않는다 — 스위퍼가 리스 만료로 되살린다
                break;
            }
            catch (ConcurrencyConflictException ex) when (ex.InnerException is DbUpdateConcurrencyException)
            {
                // **경합은 오류가 아니다.** 같은 공정을 워커 둘이 들면 진 쪽이 여기로 온다.
                // 이긴 쪽이 이미 상태를 확정했으므로 할 일이 없다 — 오류로 남기면 진짜
                // 오류를 가린다. 토큰이 없던 시절에는 두 사본의 변경이 한 행에 섞였다
                //
                // EfJobRepository.SaveChangesAsync 가 DbUpdateConcurrencyException 을
                // ConcurrencyConflictException 으로 감싸 던진다(merge-gate 리뷰 F2) — 원래
                // 예외 타입은 InnerException 으로 남아 있어 이 판정을 그대로 유지한다
                logger.LogInformation(
                    "Task {TaskId} was settled by another worker", queued.TaskId);
            }
            catch (Exception ex)
            {
                // 유스케이스 밖에서 터진 예외다. Ack 하지 않으면 같은 메시지가 무한히
                // 재배달되므로, 기록하고 넘긴다. 공정 자체는 리스 만료로 회수된다
                logger.LogError(ex, "Unhandled failure for task {TaskId}", queued.TaskId);
            }

            await AckAsync(queued, stoppingToken);
        }
    }

    private async Task AckAsync(QueuedTask queued, CancellationToken ct)
    {
        try
        {
            await queue.AckAsync(queued, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ack 실패는 중복 배달로 이어질 뿐이다. 워커가 상태를 다시 확인하므로 안전하다
            logger.LogWarning(ex, "Ack failed for task {TaskId}", queued.TaskId);
        }
    }
}
