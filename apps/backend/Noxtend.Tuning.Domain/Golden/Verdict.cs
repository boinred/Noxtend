namespace Noxtend.Tuning.Domain.Golden;

/// <summary>
/// 사람의 판정.
///
/// Design Ref: §3.2 · Plan D-8
///
/// **왜 기록하는가.** 화면에서 보고 판단한 뒤 프롬프트만 고치면, 판단 근거가 머릿속에만
/// 남는다. 한 달 뒤 "3번 버전이 2번보다 나았나" 를 되짚을 수 없고, 결국 같은 이미지를
/// 다시 보게 된다.
///
/// 합불 이진값 + 자유 메모다. 점수를 매기지 않는 이유는 척도를 정할 근거가 아직 없기
/// 때문이다 — 무엇이 좋은지 알게 된 다음에 세분화한다.
///
/// 작업 하나에 판정은 하나다. 다시 판단하면 덮어쓴다.
/// </summary>
public sealed class Verdict
{
    private Verdict()
    {
        // EF Core 재구성용
    }

    private Verdict(Guid id, Guid jobId, bool isPass, string memo, DateTimeOffset at)
    {
        Id = id;
        JobId = jobId;
        IsPass = isPass;
        Memo = memo;
        At = at;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public bool IsPass { get; private set; }
    public string Memo { get; private set; } = string.Empty;
    public DateTimeOffset At { get; private set; }

    public static Verdict Create(Guid jobId, bool isPass, string memo, DateTimeOffset at)
        => new(Guid.NewGuid(), jobId, isPass, memo.Trim(), at);

    /// <summary>재판정. 이력을 쌓지 않고 덮어쓴다 — 최신 판단이 곧 판단이다.</summary>
    public void Revise(bool isPass, string memo, DateTimeOffset at)
    {
        IsPass = isPass;
        Memo = memo.Trim();
        At = at;
    }
}
