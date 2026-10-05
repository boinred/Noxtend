using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// 분해 응답의 목록 밖 파츠 참조 관대화 (character-tuning workstream B).
///
/// **파츠 힌트로 일부만 고르면 정상 입력에서도 실패하던 것을 고친다.** 몸통은 상의·갑주에
/// 가려지는데 그 옷을 힌트에서 빼면, 분해 모델이 `occludedBy` 에 세트 밖 파츠를 가리키거나
/// 여분 파츠를 더해 작업 전체가 죽었다(실 생성에서 재현). 가림 정보는 생성 입력이 아니므로
/// (§PromptTemplate) 세트 밖 참조는 버려도 안전하고, 필수 파츠 누락만 도메인이 잡는다.
/// </summary>
public sealed class DecomposeReferenceLenienceTests
{
    // 몸통·머리·머리카락 3파츠(P01·P02·P03)를 만들고 분해 응답을 먹인다
    private static PipelineFixture WithThreePartsAndDecompose(string decomposeJson)
        => new(FakeLlmProvider.Returning(kind => kind switch
        {
            LlmOperationKind.Decompose => decomposeJson,
            LlmOperationKind.Extract => """{ "parts": ["몸통", "머리", "머리카락"] }""",
            _ => FakeLlmProvider.DefaultAnalyzeJson,
        }));

    // 파츠 한 항목 — 배치 하나(화면 안), depthOrder 는 호출자가 서로 다르게 준다
    private static string Part(string partRef, string category, int depth, string occludedBy)
        => $$"""
            {
              "partRef": "{{partRef}}",
              "category": "{{category}}",
              "description": "{{category}} 파츠",
              "placements": [{"x":0.5,"y":0.5,"w":0.2,"h":0.2}],
              "depthOrder": {{depth}},
              "occludedBy": {{occludedBy}}
            }
            """;

    private static string Decompose(params string[] parts)
        => $$"""{ "parts": [{{string.Join(",", parts)}}] }""";

    private static async Task<PipelineJob> RunToDecomposeAsync(PipelineFixture fixture)
    {
        var job = await fixture.StartJobAsync(category: AssetCategory.Character);
        await fixture.RunAllStagesAsync(job);
        await fixture.RunUntilTerminalAsync(job, TaskKind.Decompose);

        return (await fixture.Jobs.GetAsync(job.Id, CancellationToken.None))!;
    }

    /// <summary>occludedBy 의 세트 밖 참조는 버리고, 유효 참조만 남긴 채 성공한다.</summary>
    [Fact]
    public async Task OutOfSetOccludedBy_IsDroppedAndSucceeds()
    {
        // 몸통이 머리(P02)와 존재하지 않는 P06 에 가려진다고 온다 — P06 만 버려야 한다
        var fixture = WithThreePartsAndDecompose(Decompose(
            Part("P01", "몸통", 1, """["P02","P06"]"""),
            Part("P02", "머리", 2, "[]"),
            Part("P03", "머리카락", 3, "[]")));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, decompose.Status);

        // 유효 참조(머리)만 남고 유령 참조는 사라진다
        var body = job.Parts.First(p => p.Name == "몸통");
        Assert.Equal(["머리"], body.OccludedBy);
    }

    /// <summary>주어진 목록 밖 partRef 여분 항목은 버리고, 남은 필수 파츠로 성공한다.</summary>
    [Fact]
    public async Task ExtraPartRef_IsDroppedAndSucceeds()
    {
        // 모델이 상의(P04)를 임의로 더했다 — 준 세트에 없으므로 버린다
        var fixture = WithThreePartsAndDecompose(Decompose(
            Part("P01", "몸통", 1, "[]"),
            Part("P02", "머리", 2, "[]"),
            Part("P03", "머리카락", 3, "[]"),
            Part("P04", "상의", 4, "[]")));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, decompose.Status);
        Assert.Equal(3, job.Parts.Count);
    }

    /// <summary>필수 파츠가 빠지면(재번호로 사라짐) 지어내지 않고 실패한다.</summary>
    [Fact]
    public async Task MissingRequiredPart_StillFails()
    {
        // 머리카락(P03)이 재번호로 P13 이 되어 세트 밖 — 버리면 필수 파츠가 빈다
        var fixture = WithThreePartsAndDecompose(Decompose(
            Part("P01", "몸통", 1, "[]"),
            Part("P02", "머리", 2, "[]"),
            Part("P13", "머리카락", 3, "[]")));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(ErrorCode.PartNameMismatch, decompose.FailureReason);
    }

    // Extract 가 복합 파츠(가방)를 몸체·스트랩·버클 세 이름으로 이미 분리해 반환한
    // 상태를 흉내낸다 — 분리는 Extract 의 몫이라는 D-06 불변식의 전제다
    private static PipelineFixture WithCompositePartsAndDecompose(string decomposeJson)
        => new(FakeLlmProvider.Returning(kind => kind switch
        {
            LlmOperationKind.Decompose => decomposeJson,
            LlmOperationKind.Extract => """{ "parts": ["몸통", "머리", "머리카락", "가방_몸체", "가방_스트랩", "가방_버클"] }""",
            _ => FakeLlmProvider.DefaultAnalyzeJson,
        }));

    /// <summary>
    /// Extract 가 복합 파츠를 이미 여러 이름으로 나눠 놓으면, Decompose 는 그 조각들을
    /// 그대로 채울 뿐 더 만들거나 합치지 않는다(character-studio §D-06 불변식).
    /// </summary>
    [Fact]
    public async Task CompositePartSplitByExtract_DecomposeFillsWithoutAdding()
    {
        var fixture = WithCompositePartsAndDecompose(Decompose(
            Part("P01", "몸통", 1, "[]"),
            Part("P02", "머리", 2, "[]"),
            Part("P03", "머리카락", 3, "[]"),
            Part("P04", "가방_몸체", 4, "[]"),
            Part("P05", "가방_스트랩", 5, """["P04"]"""),
            Part("P06", "가방_버클", 6, """["P04"]""")));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, decompose.Status);

        // Extract 가 이미 낸 6조각 그대로 — Decompose 가 더하지도 합치지도 않는다
        Assert.Equal(6, job.Parts.Count);
        var strap = job.Parts.First(p => p.Name == "가방_스트랩");
        Assert.Equal(["가방_몸체"], strap.OccludedBy);
    }
}
