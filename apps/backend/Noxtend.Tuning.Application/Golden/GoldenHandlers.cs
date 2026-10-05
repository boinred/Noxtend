using Noxtend.Domain.Common;
using Noxtend.Domain.Ports;
using Noxtend.Tuning.Domain.Golden;
using Noxtend.Tuning.Domain.Ports;

namespace Noxtend.Tuning.Application.Golden;

public sealed class ListGoldenSamplesHandler(IGoldenSampleRepository samples)
{
    public Task<IReadOnlyList<GoldenSample>> HandleAsync(CancellationToken ct)
        => samples.ListAsync(ct);
}

/// <summary>
/// 골든 샘플 등록.
///
/// Design Ref: §3.2 · FR-11 — 기존 업로드를 재사용한다.
///
/// 이미지를 새로 받지 않는 이유는 저장 경로가 둘이 되면 둘 다 관리해야 하기 때문이다.
/// 스튜디오에서 한 번 돌려본 이미지를 그대로 골든으로 승격하는 흐름이 자연스럽기도 하다.
/// </summary>
public sealed class CreateGoldenSampleHandler(
    IGoldenSampleRepository samples,
    IStoredImageRepository images,
    IClock clock)
{
    public async Task<Result<GoldenSample>> HandleAsync(
        Guid storedImageId,
        string name,
        string expectedNote,
        CancellationToken ct)
    {
        // 없는 업로드를 가리키면 비교 화면이 이미지를 못 띄운다 — 등록 시점에 막는다
        if (await images.GetAsync(storedImageId, ct) is null)
        {
            return Result<GoldenSample>.Fail(
                ErrorCode.GoldenImageNotFound, "업로드를 찾을 수 없습니다");
        }

        var sample = GoldenSample.Create(storedImageId, name, expectedNote, clock.Now);

        await samples.AddAsync(sample, ct);
        await samples.SaveChangesAsync(ct);

        return Result<GoldenSample>.Ok(sample);
    }
}

public sealed class UpdateGoldenSampleHandler(IGoldenSampleRepository samples)
{
    public async Task<Result<GoldenSample>> HandleAsync(
        Guid id,
        string name,
        string expectedNote,
        CancellationToken ct)
    {
        var sample = await samples.GetAsync(id, ct);
        if (sample is null)
        {
            return Result<GoldenSample>.Fail(ErrorCode.GoldenNotFound, "골든 샘플을 찾을 수 없습니다");
        }

        sample.Update(name, expectedNote);
        await samples.SaveChangesAsync(ct);

        return Result<GoldenSample>.Ok(sample);
    }
}

/// <summary>
/// 삭제.
///
/// **실행 기록은 지우지 않는다.** 골든 샘플은 "이 이미지를 기준으로 삼는다" 는 표시일
/// 뿐이고, 그 이미지로 돌린 작업과 판정은 그 자체로 유효한 기록이다.
/// </summary>
public sealed class DeleteGoldenSampleHandler(IGoldenSampleRepository samples)
{
    public async Task<Result<bool>> HandleAsync(Guid id, CancellationToken ct)
    {
        var sample = await samples.GetAsync(id, ct);
        if (sample is null)
        {
            return Result<bool>.Fail(ErrorCode.GoldenNotFound, "골든 샘플을 찾을 수 없습니다");
        }

        await samples.RemoveAsync(sample, ct);
        await samples.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }
}

/// <summary>
/// 사람의 판정 기록 (FR-14).
///
/// 작업 하나에 판정 하나다. 다시 판단하면 덮어쓴다 — 이력을 쌓으면 "지금 판단이 무엇인가"
/// 를 매번 계산해야 하고, 그 계산이 화면마다 달라진다.
/// </summary>
public sealed class RecordVerdictHandler(IVerdictRepository verdicts, IClock clock)
{
    public async Task<Result<Verdict>> HandleAsync(
        Guid jobId,
        bool isPass,
        string memo,
        CancellationToken ct)
    {
        var existing = await verdicts.GetByJobAsync(jobId, ct);

        if (existing is not null)
        {
            existing.Revise(isPass, memo, clock.Now);
            await verdicts.SaveChangesAsync(ct);
            return Result<Verdict>.Ok(existing);
        }

        var verdict = Verdict.Create(jobId, isPass, memo, clock.Now);
        await verdicts.AddAsync(verdict, ct);
        await verdicts.SaveChangesAsync(ct);

        return Result<Verdict>.Ok(verdict);
    }
}
