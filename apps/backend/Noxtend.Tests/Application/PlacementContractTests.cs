using System.Text.Json;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Infrastructure.Llm;

namespace Noxtend.Tests.Application;

/// <summary>
/// 분해 응답의 배치 계약 (Design §5 · FR-01·FR-02·FR-09).
///
/// **전에는 파츠당 상자가 하나였다.** 가로등이 여덟 개인데 하나만 낼 수 있으면 모델이 낼 수
/// 있는 정직한 답은 전부를 감싸는 합집합뿐이고, 실제로 `w=0.94` 같은 값이 나왔다. 스키마가
/// 사실을 표현할 수 없었던 것이지 모델이 틀린 게 아니다.
/// </summary>
public sealed class PlacementContractTests
{
    /// <summary>파츠 이름 하나짜리 작업에 분해 응답을 먹인다.</summary>
    private static PipelineFixture WithDecomposeJson(string json)
        => new(FakeLlmProvider.Returning(kind => kind switch
        {
            LlmOperationKind.Decompose => json,
            LlmOperationKind.Extract => """{ "parts": ["가로수"] }""",
            _ => FakeLlmProvider.DefaultAnalyzeJson,
        }));

    private static string Decompose(string placementsJson) => $$"""
        {
          "parts": [{
            "partRef": "P01",
            "category": "식생",
            "description": "잎이 무성한 가로수",
            "placements": {{placementsJson}},
            "depthOrder": 1,
            "occludedBy": []
          }]
        }
        """;

    /// <summary>분해까지 돌리고 저장된 작업을 되읽는다 — 앞 공정이 파츠를 만들어야 한다.</summary>
    private static async Task<PipelineJob> RunToDecomposeAsync(PipelineFixture fixture)
    {
        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);
        await fixture.RunUntilTerminalAsync(job, TaskKind.Decompose);

        return (await fixture.Jobs.GetAsync(job.Id, CancellationToken.None))!;
    }

    /// <summary>B-01 — 배치 셋이 모델이 낸 순서 그대로 들어온다 (FR-09).</summary>
    [Fact]
    public async Task SeveralPlacements_KeepTheirOrder()
    {
        var fixture = WithDecomposeJson(Decompose("""
            [{"x":0.66,"y":0.64,"w":0.04,"h":0.10},
             {"x":0.10,"y":0.60,"w":0.05,"h":0.12},
             {"x":0.42,"y":0.58,"w":0.03,"h":0.07}]
            """));

        var job = await RunToDecomposeAsync(fixture);

        // x 오름차순이 아닌 순서로 넣었다 — 정렬해 버리면 여기서 걸린다
        Assert.Equal(
            [0.66, 0.10, 0.42],
            Assert.Single(job.Parts).Placements.Select(p => p.X));
    }

    /// <summary>B-02·B-04 — 계약 위반은 조용히 넘어가지 않는다.</summary>
    [Theory]
    // 배열이 아니다
    [InlineData("""{"x":0.1,"y":0.1,"w":0.1,"h":0.1}""")]
    // 구형 단일 상자가 다시 들어오면 계약이 되돌아간 것이다
    [InlineData("null")]
    // 항목에 w 가 없다
    [InlineData("""[{"x":0.1,"y":0.1,"h":0.1}]""")]
    // 항목이 객체가 아니다
    [InlineData("""["0.1,0.1,0.1,0.1"]""")]
    public async Task InvalidPlacements_FailWithProviderBadResponse(string placementsJson)
    {
        var fixture = WithDecomposeJson(Decompose(placementsJson));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(ErrorCode.ProviderBadResponse, decompose.FailureReason);
    }

    /// <summary>B-09 — v3 스키마가 배열을 요구한다.</summary>
    [Fact]
    public void DecomposeV3SchemaRequiresPlacements()
    {
        var (system, _, schema, _) = SeedPrompts.DecomposeV3();
        using var document = JsonDocument.Parse(schema);
        var item = document.RootElement
            .GetProperty("properties").GetProperty("parts").GetProperty("items");
        var placements = item.GetProperty("properties").GetProperty("placements");

        Assert.Equal("array", placements.GetProperty("type").GetString());
        Assert.Equal(1, placements.GetProperty("minItems").GetInt32());
        Assert.Equal(20, placements.GetProperty("maxItems").GetInt32());
        Assert.Equal(
            ["x", "y", "w", "h"],
            placements.GetProperty("items").GetProperty("required")
                .EnumerateArray().Select(v => v.GetString()));

        // `bounds` 가 남아 있으면 두 계약이 공존해 모델이 어느 쪽을 낼지 모른다
        Assert.False(item.GetProperty("properties").TryGetProperty("bounds", out _));

        // **첫 줄이 나머지를 지배한다** — "한 번 그려 여러 곳에 놓는다" 가 없으면
        // "한 장으로 덮이는 것만 묶어라" 가 임의의 제약으로 읽힌다
        Assert.Contains("drawn once", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("placements", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>v2 는 과거 호출 재현용으로 손대지 않는다 (NFR-04).</summary>
    [Fact]
    public void DecomposeV2KeepsTheSingleBox()
    {
        var (_, _, schema, _) = SeedPrompts.DecomposeV2();
        using var document = JsonDocument.Parse(schema);

        Assert.True(document.RootElement
            .GetProperty("properties").GetProperty("parts").GetProperty("items")
            .GetProperty("properties").TryGetProperty("bounds", out _));
    }

    /// <summary>
    /// 화면을 벗어나는 배치는 버린다 (사이클 #9 Cycle 6).
    ///
    /// **실측에서 나온 규칙이다.** 배치가 파츠당 하나이던 시절엔 작업당 상자가 9개라 경계
    /// 초과가 드물었다. 배치가 95개가 되자 세 번 연속으로 실패했고, 매번 다른 상자가 넘쳤다 —
    /// 1.01 · 1.05 · 1.07. 재시도로 넘을 수 있는 벽이 아니다.
    ///
    /// **자르지 않는 이유는 배치가 합성 지시이기 때문이다.** 잘라 넣으면 온전한 그림을 반쪽
    /// 상자에 밀어 넣게 되어 합성 결과가 눌린다.
    /// </summary>
    [Fact]
    public async Task PlacementCrossingTheEdge_IsDropped()
    {
        var fixture = WithDecomposeJson(Decompose("""
            [{"x":0.10,"y":0.10,"w":0.20,"h":0.20},
             {"x":0.93,"y":0.68,"w":0.14,"h":0.10}]
            """));

        var job = await RunToDecomposeAsync(fixture);

        // 화면 안에 온전히 들어가는 것만 남는다
        var placement = Assert.Single(Assert.Single(job.Parts).Placements);
        Assert.Equal(0.10, placement.X);
    }

    /// <summary>
    /// 전부 버려지면 그 파츠는 놓을 자리가 없다 — 조용히 사라지지 않는다.
    ///
    /// 모델이 좌표계를 통째로 잘못 이해한 경우가 이렇게 드러난다.
    /// </summary>
    [Fact]
    public async Task PartWhosePlacementsAreAllOutside_Fails()
    {
        var fixture = WithDecomposeJson(Decompose("""
            [{"x":0.93,"y":0.68,"w":0.14,"h":0.10}]
            """));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(ErrorCode.PartBoundsOutOfRange, decompose.FailureReason);
    }

    /// <summary>
    /// 시작점이 화면 밖이면 자르지 않고 거부한다.
    ///
    /// 가장자리에 걸친 것과 좌표계를 잘못 이해한 것은 다른 일이다. 뒤엣것을 조용히 고치면
    /// 모델이 계속 틀린 채로 남는다.
    /// </summary>
    [Theory]
    [InlineData("""[{"x":1.2,"y":0.1,"w":0.1,"h":0.1}]""")]
    [InlineData("""[{"x":-0.1,"y":0.1,"w":0.2,"h":0.1}]""")]
    [InlineData("""[{"x":0.1,"y":1.4,"w":0.1,"h":0.1}]""")]
    public async Task PlacementStartingOutsideTheFrame_IsRejected(string placementsJson)
    {
        var fixture = WithDecomposeJson(Decompose(placementsJson));

        var job = await RunToDecomposeAsync(fixture);

        var decompose = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(ErrorCode.PartBoundsOutOfRange, decompose.FailureReason);
    }
}
