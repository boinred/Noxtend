namespace Noxtend.Tuning.Domain.Golden;

/// <summary>
/// 골든 샘플 — 평가의 기준선.
///
/// Design Ref: §3.2 · Plan D-4
///
/// **골든 실행에 플래그를 두지 않는다.** 이 샘플의 <see cref="StoredImageId"/> 로 돌린
/// 작업이 곧 골든 실행이다 — `Job.SourceImageId` 로 조회하면 된다. 작업에 "골든 여부"
/// 필드를 더하면 동기화할 것이 하나 늘고, 나중에 샘플을 지웠을 때 어긋난다.
///
/// 이미지를 새로 저장하지 않고 기존 업로드(<see cref="StoredImageId"/>)를 재사용하는 것도
/// 같은 이유다 — 저장 경로가 둘이 되면 둘 다 관리해야 한다.
/// </summary>
public sealed class GoldenSample
{
    private GoldenSample()
    {
        // EF Core 재구성용
    }

    private GoldenSample(
        Guid id,
        Guid storedImageId,
        string name,
        string expectedNote,
        DateTimeOffset now)
    {
        Id = id;
        StoredImageId = storedImageId;
        Name = name;
        ExpectedNote = expectedNote;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid StoredImageId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// "등대 · 부두 · 어선이 나와야 하고, 하늘은 파츠가 아니다" 처럼 사람이 적는 기대.
    ///
    /// **구조화하지 않는다.** 무엇이 좋은 분해인지 아직 모르므로(Plan D-3) 기준을 지금
    /// 형식으로 굳히면 잘못된 기준을 고착시킨다. 사람이 읽고 사람이 판단한다.
    /// </summary>
    public string ExpectedNote { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static GoldenSample Create(
        Guid storedImageId,
        string name,
        string expectedNote,
        DateTimeOffset now)
        => new(Guid.NewGuid(), storedImageId, name.Trim(), expectedNote.Trim(), now);

    public void Update(string name, string expectedNote)
    {
        Name = name.Trim();
        ExpectedNote = expectedNote.Trim();
    }
}
