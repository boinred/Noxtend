using Microsoft.Extensions.Logging;
using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;

namespace Noxtend.Application.Pipeline;

/// <summary>
/// 끝난 작업을 **완전히** 지운다.
///
/// **종료 상태만 지울 수 있다.** 진행 중인 작업은 먼저 취소해야 하는데, 그 판정을
/// 읽어서 확인하고 따로 지우면 그 사이에 재시도나 3D 추가가 작업을 다시 열 수 있다.
/// 그때 삭제가 그대로 진행되면 유료 외부 작업이 돌고 있는데 그것을 추적할 기록이
/// 사라진다. 그래서 조건을 삭제문에 실어 저장소가 한 번에 판정한다.
///
/// **흔적을 남기지 않는다.** 작업과 소유물(공정·파츠·생성물)뿐 아니라 외부 실행
/// 기록과 **비용 원장(<c>LlmCalls</c>)**, 그리고 저장소의 파일까지 지운다.
/// 그 대가로 **사용량 화면의 지난 총액이 줄어든다** — 지운 작업에 쓴 돈이 집계에서
/// 함께 사라진다는 뜻이다.
///
/// **파일은 기록 뒤에 지운다.** 저장소는 DB 트랜잭션에 들어가지 못하므로 둘 중 하나는
/// 먼저여야 한다. 기록을 먼저 확정하면 최악의 경우 고아 파일이 남는데, 그것은 사용자에게
/// 안 보이고 나중에 치울 수 있다. 반대 순서라면 **내려받기가 깨진 작업이 목록에 남는다.**
/// </summary>
public sealed class DeleteJobHandler(
    IJobRepository jobs,
    IBlobStorage images,
    IMeshArtifactStorage meshes,
    ILogger<DeleteJobHandler> logger)
{
    public async Task<Result<Unit>> HandleAsync(Guid jobId, CancellationToken ct)
    {
        // 대상 존재 확인
        if (await jobs.GetAsync(jobId, ct) is null)
        {
            return Result<Unit>.Fail(ErrorCode.JobNotFound, "작업을 찾을 수 없습니다");
        }

        // 종료 상태 조건부 삭제
        var blobs = await jobs.DeleteIfTerminalAsync(jobId, ct);

        if (blobs is null)
        {
            // 존재는 하는데 안 지워졌다 — 그 사이 다시 열렸거나 처음부터 진행 중이었다
            return Result<Unit>.Fail(
                ErrorCode.JobActiveCannotDelete,
                "진행 중인 작업은 삭제할 수 없습니다. 먼저 취소해 주세요");
        }

        await RemoveAsync(blobs.Images, images.DeleteAsync, jobId, ct);
        await RemoveAsync(blobs.Meshes, meshes.DeleteAsync, jobId, ct);

        return Result<Unit>.Ok(Unit.Value);
    }

    /// <summary>
    /// 파일을 최선으로 지운다.
    ///
    /// **실패해도 삭제는 이미 확정이다.** 기록이 없어진 뒤라 되돌릴 것이 없고, 여기서
    /// 오류를 올리면 사용자는 지워진 작업을 두고 "삭제 실패" 를 보게 된다.
    /// 남은 파일은 로그로 남겨 나중에 치운다.
    /// </summary>
    private async Task RemoveAsync(
        IReadOnlyList<string> keys,
        Func<string, CancellationToken, Task> delete,
        Guid jobId,
        CancellationToken ct)
    {
        foreach (var key in keys)
        {
            try
            {
                await delete(key, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex, "Orphaned blob after deleting job {JobId}: {BlobKey}", jobId, key);
            }
        }
    }
}
