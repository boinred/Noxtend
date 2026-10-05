using System.Text.Json;
using Noxtend.Application.Common;
using Noxtend.Application.Stages;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;
using Noxtend.Tests.Domain;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.1 #6~#10 · Plan FR-05 · D-9 — 유효성 검사.
///
/// **Check 단계에서 이 테스트들이 없다는 것이 드러났다** (G-2). 오류 코드는 코드에
/// 있었지만 **실제로 발동하는지 아무도 확인하지 않았다.** Plan D-9 가 "기계가 막는다" 고
/// 선언한 부분인데 그 선언을 증명하는 것이 없었다.
///
/// 유효성은 품질 판단과 다르다. 여기 있는 것은 전부 명백한 오류이고, 사람의 눈은
/// "서술이 쓸 만한가" 에 써야 한다.
/// </summary>
public sealed class SceneValidationTests
{
    private static PipelineFixture WithSceneJson(string json)
        => new(FakeLlmProvider.Returning(kind => kind == LlmOperationKind.Analyze ? json : "{}"));

    /// <summary>완전한 장면. 필드를 하나씩 빼서 검사를 태운다.</summary>
    private const string CompleteScene = """
        {
          "palette": [
            { "name": "청회색 바다", "hex": "#2e5c6e" },
            { "name": "주황색 목재", "hex": "#D97A3C" },
            { "name": "옅은 안개", "hex": "#F2E8DC" }
          ],
          "timeOfDay": "해질녘", "mood": "고요", "renderingStyle": "수채", "materialFeel": "목재",
          "camera": { "type": "one-point", "eyeLevel": "1.6m", "horizonY": 0.55 },
          "light": { "direction": "좌측 후방", "temperature": "따뜻함", "shadowHardness": "부드러움" },
          "scaleReference": { "object": "기둥", "realWorldSize": "3m", "heightMeters": 3 }
        }
        """;

    /// <summary>팔레트만 갈아끼운 장면 — 나머지 필드는 통과 상태로 둬야 팔레트 규칙만 걸린다.</summary>
    private static string WithPalette(string paletteJson)
    {
        var node = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(CompleteScene)!;
        node["palette"] = JsonSerializer.Deserialize<JsonElement>(paletteJson);
        return JsonSerializer.Serialize(node);
    }

    private static string Without(string field)
    {
        var node = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(CompleteScene)!;
        node.Remove(field);
        return JsonSerializer.Serialize(node);
    }

    [Fact]
    public async Task CompleteScene_Succeeds()
    {
        // 기준선 — 완전한 장면은 통과해야 나머지 단정이 의미를 갖는다
        var fixture = WithSceneJson(CompleteScene);
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, job.Tasks[0].Status);
        Assert.Equal(0.55, job.Scene!.Camera.HorizonY);
        Assert.Equal(3, job.Scene.Scale.HeightMeters);
    }

    /// <summary>배경의 구조화 높이는 실제 보정 계산에 쓰이므로 유한한 현실 범위 양수만 받는다.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("10001")]
    [InlineData("\"3\"")]
    [InlineData("null")]
    public async Task InvalidBackgroundScaleHeight_FailsWithProviderBadResponse(string heightJson)
    {
        var json = CompleteScene.Replace("\"heightMeters\": 3", $"\"heightMeters\": {heightJson}");
        var fixture = WithSceneJson(json);
        var job = await fixture.StartJobAsync(category: AssetCategory.Background);

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.ProviderBadResponse, job.Tasks[0].FailureReason);
        Assert.Null(job.Scene);
    }

    /// <summary>Character/Object와 과거 계약은 numeric scale 없이도 기존 분석을 유지한다.</summary>
    [Fact]
    public async Task ObjectAnalyze_AllowsLegacyScaleWithoutHeightMeters()
    {
        var json = CompleteScene.Replace(", \"heightMeters\": 3", string.Empty);
        var fixture = WithSceneJson(json);
        var job = await fixture.StartJobAsync(category: AssetCategory.Object);

        await fixture.RunFirstStageAsync(job);

        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, job.Tasks[0].Status);
        Assert.Null(job.Scene!.Scale.HeightMeters);
    }

    /// <summary>배경 Extract는 scale 기준 이름을 그대로 파츠 정본에 연결해야 한다.</summary>
    [Fact]
    public void BackgroundExtract_RejectsMissingExactScaleAnchor()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), DateTimeOffset.UtcNow);
        job.ApplyScene(TestScene.Default);
        var stage = new ExtractStage();

        Assert.Throws<ProviderBadResponseException>(() =>
            stage.Interpret(job, """{"parts":["부두","부두 기둥 축약"]}"""));
    }

    /// <summary>
    /// B-01 — 팔레트가 이름과 색으로 나뉘어 들어오고 HEX 가 대문자로 정규화된다.
    ///
    /// 이름과 색을 한 문자열에 담아 두면 화면이 매번 다시 파싱해야 하고, 모델이 자연어만
    /// 주는 순간 색이 사라진다. 정본을 여기서 나눠 둔다 (D-01).
    /// </summary>
    [Fact]
    public async Task StructuredPalette_SplitsNameAndHex()
    {
        var fixture = WithSceneJson(CompleteScene);
        var job = await fixture.StartJobAsync();

        await fixture.RunFirstStageAsync(job);

        // 모델이 소문자로 줘도 저장은 대문자다 — 같은 색이 두 표기로 남으면 비교가 안 된다
        Assert.Equal(
            [
                new PaletteEntry("청회색 바다", "#2E5C6E"),
                new PaletteEntry("주황색 목재", "#D97A3C"),
                new PaletteEntry("옅은 안개", "#F2E8DC"),
            ],
            job.Scene!.Palette);
    }

    /// <summary>
    /// B-02~B-06 — 팔레트 계약 위반은 조용히 넘어가지 않는다 (FR-05 · Design §10).
    ///
    /// **전에는 팔레트가 선택 필드였다.** 없으면 빈 배열로 두고 지나갔고, 그래서 색이
    /// 통째로 빠진 장면이 성공으로 기록됐다. 잘못된 색을 투명 칩으로 숨기던 화면 쪽 결함과
    /// 같은 뿌리다 — 값이 없다는 사실을 아무도 말하지 않았다.
    ///
    /// 전부 `PROVIDER_BAD_RESPONSE` 다. 구조화 출력 스키마를 준 뒤에도 어긋난 것이므로
    /// 공급자가 계약을 어긴 것이고, 다시 돌리면 대개 맞는다.
    /// </summary>
    [Theory]
    // B-03 — 개수 하한·상한
    [InlineData("""[{"name":"바다","hex":"#2E5C6E"},{"name":"목재","hex":"#D97A3C"}]""")]
    [InlineData("""
        [{"name":"1","hex":"#111111"},{"name":"2","hex":"#222222"},{"name":"3","hex":"#333333"},
         {"name":"4","hex":"#444444"},{"name":"5","hex":"#555555"},{"name":"6","hex":"#666666"},
         {"name":"7","hex":"#777777"},{"name":"8","hex":"#888888"},{"name":"9","hex":"#999999"}]
        """)]
    // B-04 — 구형 문자열 배열. 이 모양이 다시 들어오면 계약이 되돌아간 것이다
    [InlineData("""["#2E5C6E","#D97A3C","#F2E8DC"]""")]
    // B-05 — 이름이 공백뿐이면 화면에 빈 칸이 선다
    [InlineData("""[{"name":"  ","hex":"#2E5C6E"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    // B-06 — 짧은 표기·`#` 누락·잘못된 문자·알파 채널
    [InlineData("""[{"name":"바다","hex":"#FFF"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    [InlineData("""[{"name":"바다","hex":"2E5C6E"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    [InlineData("""[{"name":"바다","hex":"#GGGGGG"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    [InlineData("""[{"name":"바다","hex":"#2E5C6EFF"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    // hex 필드 자체가 없는 경우
    [InlineData("""[{"name":"바다"},{"name":"목재","hex":"#D97A3C"},{"name":"안개","hex":"#F2E8DC"}]""")]
    public async Task InvalidPalette_FailsWithProviderBadResponse(string paletteJson)
    {
        var fixture = WithSceneJson(WithPalette(paletteJson));
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.ProviderBadResponse, job.Tasks[0].FailureReason);
        Assert.Null(job.Scene);
    }

    /// <summary>B-02 — 팔레트가 아예 없는 응답.</summary>
    [Fact]
    public async Task MissingPalette_FailsWithProviderBadResponse()
    {
        var fixture = WithSceneJson(Without("palette"));
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.ProviderBadResponse, job.Tasks[0].FailureReason);
        Assert.Null(job.Scene);
    }

    // §8.1 #6 — 조립을 좌우하는 셋
    [Theory]
    [InlineData("camera")]
    [InlineData("light")]
    [InlineData("scaleReference")]
    public async Task MissingRequiredField_FailsWithSceneIncomplete(string field)
    {
        var fixture = WithSceneJson(Without(field));
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        // **PROVIDER_BAD_RESPONSE 가 아니다.** 형식은 맞고 필드가 없는 것이며,
        // 사용자가 할 일은 재시도가 아니라 프롬프트 수정이다 (G-1)
        Assert.Equal(ErrorCode.SceneIncomplete, job.Tasks[0].FailureReason);
        Assert.Null(job.Scene);
    }

    [Fact]
    public async Task BrokenJson_StaysProviderBadResponse()
    {
        // 두 코드가 실제로 구분되는지 — 하나로 합쳐지면 구분한 의미가 없다
        var fixture = WithSceneJson("이건 JSON 이 아니다");
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        Assert.Equal(ErrorCode.ProviderBadResponse, job.Tasks[0].FailureReason);
    }

    /// <summary>
    /// **재시도가 실제로 값을 내는가.**
    ///
    /// LLM 이 한 번 흘린 것 때문에 10분짜리 작업을 버리는 것은 아깝다 —
    /// 이 재시도를 넣은 이유가 그것이다.
    /// </summary>
    [Fact]
    public async Task TransientSlip_RecoversOnRetry()
    {
        var attempt = 0;
        var fixture = new PipelineFixture(FakeLlmProvider.Returning(kind =>
        {
            if (kind != LlmOperationKind.Analyze) return "{}";
            // 첫 시도만 필드를 빠뜨린다
            return ++attempt == 1 ? Without("camera") : CompleteScene;
        }));

        var job = await fixture.StartJobAsync();
        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Succeeded, analyze.Status);
        Assert.Equal(2, analyze.AttemptCount);
        Assert.NotNull(job.Scene);
    }

    [Fact]
    public async Task RetriesStopAtTheLimit()
    {
        // 프롬프트가 잘못됐으면 몇 번을 돌려도 같다. 무한히 돌면 비용만 태운다
        var fixture = WithSceneJson(Without("camera"));
        var job = await fixture.StartJobAsync();

        await fixture.RunUntilTerminalAsync(job, TaskKind.Analyze);

        var analyze = job.Tasks.First(t => t.Kind == TaskKind.Analyze);
        Assert.Equal(Noxtend.Domain.Job.TaskStatus.Failed, analyze.Status);
        Assert.Equal(fixture.Options.MaxAttempts, analyze.AttemptCount);
    }
}

/// <summary>
/// Design Ref: §8.1 #7~#10 — 파츠 유효성.
///
/// 엔티티(<see cref="PipelineJob.ValidatePartDetails"/>)를 직접 부른다. 파이프라인을
/// 통과시키면 앞 두 공정을 매번 돌려야 하고, 검증 대상은 규칙 자체다.
/// </summary>
public sealed class PartValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>파츠 둘을 가진 작업. 분해 직전 상태다.</summary>
    private static PipelineJob JobWithParts(params string[] names)
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        job.ApplyScene(TestScene.Default);
        job.ApplyParts(names.Length > 0 ? names : ["등대", "부두"]);
        return job;
    }

    private static PartDetail Detail(
        string name,
        Bounds? bounds = null,
        int depth = 1,
        params string[] occludedBy)
        => new(name, "구조물", $"{name} 서술", [bounds ?? new Bounds(0.1, 0.1, 0.2, 0.2)],
            depth, occludedBy);

    /// <summary>배치를 여러 개 가진 명세 — 같은 에셋을 여러 자리에 놓는다.</summary>
    private static PartDetail WithPlacements(string name, params Bounds[] placements)
        => new(name, "구조물", $"{name} 서술", placements, 1, []);

    [Fact]
    public void ValidDetails_Pass()
    {
        var job = JobWithParts();

        job.ValidatePartDetails([Detail("등대"), Detail("부두", depth: 2)]);
    }

    /// <summary>
    /// 배치 여럿이 정상이다 (FR-01·FR-03).
    ///
    /// 가로수 네 그루면 나무를 한 번 그리고 네 자리에 놓는다 — 그것이 이 사이클의 목적이다.
    /// </summary>
    [Fact]
    public void SeveralPlacements_Pass()
    {
        var job = JobWithParts("가로수", "부두");

        job.ValidatePartDetails([
            WithPlacements("가로수",
                new Bounds(0.31, 0.62, 0.04, 0.09),
                new Bounds(0.48, 0.58, 0.03, 0.07),
                new Bounds(0.66, 0.64, 0.04, 0.10)),
            Detail("부두", depth: 2),
        ]);
    }

    /// <summary>
    /// B-03 — 배치가 0개면 그 파츠는 놓을 자리가 없다 (FR-05).
    ///
    /// 빈 목록을 통과시키면 화면에 이름만 있고 위치가 없는 파츠가 생기고, 그것은
    /// 고치기 전 상태와 같다.
    /// </summary>
    [Fact]
    public void NoPlacement_IsRejected()
    {
        var job = JobWithParts();

        var error = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([WithPlacements("등대"), Detail("부두", depth: 2)]));

        Assert.Equal(PartValidationError.BoundsOutOfRange, error.Error);
    }

    /// <summary>B-03 — 상한 20개 (D-04).</summary>
    [Fact]
    public void TooManyPlacements_AreRejected()
    {
        var job = JobWithParts();
        var twentyOne = Enumerable
            .Range(0, 21)
            .Select(i => new Bounds(0.01 * i, 0.1, 0.01, 0.01))
            .ToArray();

        var error = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([WithPlacements("등대", twentyOne), Detail("부두", depth: 2)]));

        Assert.Equal(PartValidationError.BoundsOutOfRange, error.Error);
    }

    /// <summary>
    /// B-05 — 이탈한 배치가 **몇 번째인지** 말한다 (D-06).
    ///
    /// 배치가 스무 개까지 올 수 있는데 "좌표가 화면을 벗어납니다" 만 나오면 어느 것을 고쳐야
    /// 할지 알 수 없다. 파츠 전체를 실패시키되 위치는 밝힌다.
    /// </summary>
    [Fact]
    public void OutOfFramePlacement_NamesItsIndex()
    {
        var job = JobWithParts();

        var error = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([
                WithPlacements("등대",
                    new Bounds(0.1, 0.1, 0.2, 0.2),
                    new Bounds(0.9, 0.1, 0.5, 0.2)),   // 오른쪽으로 넘친다
                Detail("부두", depth: 2),
            ]));

        Assert.Equal(PartValidationError.BoundsOutOfRange, error.Error);
        Assert.Contains("[1]", error.Message, StringComparison.Ordinal);
    }

    // §8.1 #7 — 좌표 범위
    [Theory]
    [InlineData(-0.01, 0.1, 0.2, 0.2)]   // x 음수
    [InlineData(0.1, -0.01, 0.2, 0.2)]   // y 음수
    [InlineData(0.9, 0.1, 0.2, 0.2)]     // 오른쪽 끝이 1 초과
    [InlineData(0.1, 0.9, 0.2, 0.2)]     // 아래쪽 끝이 1 초과
    [InlineData(0.1, 0.1, 0, 0.2)]       // 폭 0 — 그릴 영역이 없다
    public void BoundsOutsideFrame_Rejected(double x, double y, double w, double h)
    {
        var job = JobWithParts();

        var ex = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([
                Detail("등대", new Bounds(x, y, w, h)),
                Detail("부두", depth: 2),
            ]));

        Assert.Equal(PartValidationError.BoundsOutOfRange, ex.Error);
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]       // 화면 전체를 채우는 파츠는 정상이다
    [InlineData(0.8, 0.8, 0.2, 0.2)] // 우하단 끝에 정확히 닿는다
    public void BoundsTouchingEdges_Accepted(double x, double y, double w, double h)
    {
        // 경계값이 거부되면 정상 결과가 실패한다 (Plan R-7)
        var job = JobWithParts();

        job.ValidatePartDetails([
            Detail("등대", new Bounds(x, y, w, h)),
            Detail("부두", depth: 2),
        ]);
    }

    // §8.1 #8 — 없는 파츠 참조
    [Fact]
    public void OccludedByUnknownPart_Rejected()
    {
        var job = JobWithParts();

        var ex = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([
                Detail("등대", occludedBy: "유령 파츠"),
                Detail("부두", depth: 2),
            ]));

        Assert.Equal(PartValidationError.UnknownReference, ex.Error);
        Assert.Contains("유령 파츠", ex.Message);
    }

    [Fact]
    public void OccludedByKnownPart_Accepted()
    {
        var job = JobWithParts();

        job.ValidatePartDetails([
            Detail("등대", occludedBy: "부두"),
            Detail("부두", depth: 2),
        ]);
    }

    /// <summary>
    /// 실제로 겪은 실패다 — `gpt-5.6-luna` 가 파츠 하나를 두 번 서술했다.
    /// 이름은 전부 맞았고 개수만 하나 많았는데 `PART_NAME_MISMATCH` 로 나가,
    /// 사용자가 "이름 지시" 를 고치려 들게 만들었다.
    /// </summary>
    [Fact]
    public void DuplicatePart_RejectedAsDuplicateNotMismatch()
    {
        var job = JobWithParts();

        var ex = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([
                Detail("등대", depth: 1),
                Detail("등대", depth: 2),
                Detail("부두", depth: 3),
            ]));

        Assert.Equal(PartValidationError.Duplicate, ex.Error);
        Assert.Contains("등대", ex.Message);
        Assert.Contains("2번", ex.Message);
    }

    // §8.1 #9 — 이름 불일치
    [Fact]
    public void RenamedPart_Rejected()
    {
        // 분해가 이름을 바꾸면 두 단계의 결과가 어긋나고,
        // 뒤 단계는 어느 쪽을 믿을지 알 수 없다
        var job = JobWithParts();

        var ex = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([Detail("등대탑"), Detail("부두", depth: 2)]));

        Assert.Equal(PartValidationError.NameMismatch, ex.Error);
    }

    [Theory]
    [InlineData(1)]   // 하나 빠짐
    [InlineData(3)]   // 하나 더함
    public void PartCountMismatch_Rejected(int count)
    {
        var job = JobWithParts();

        var details = Enumerable.Range(0, count)
            .Select(i => Detail(i < 2 ? new[] { "등대", "부두" }[i] : "새 파츠", depth: i + 1))
            .ToList();

        var ex = Assert.Throws<PartValidationException>(() => job.ValidatePartDetails(details));

        Assert.Equal(PartValidationError.NameMismatch, ex.Error);
    }

    // §8.1 #10 — 깊이 중복 (배경 — 3D 조립이 z 좌표에 DepthOrder 를 직접 쓴다)
    [Fact]
    public void DuplicateDepthOrder_Rejected()
    {
        // 깊이가 겹치면 앞뒤 순서를 정의하지 못한다 — 조립 단계가 쌓을 수 없다
        var job = JobWithParts();

        var ex = Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([Detail("등대", depth: 1), Detail("부두", depth: 1)]));

        Assert.Equal(PartValidationError.DepthDuplicate, ex.Error);
        Assert.Contains("등대", ex.Message);
        Assert.Contains("부두", ex.Message);
    }

    /// <summary>
    /// 캐릭터는 DepthOrder 중복을 허용한다 (2026-08-31 결정,
    /// docs/specs/2026-08-31-character-depth-duplicate-allowed.md).
    ///
    /// **캐릭터에는 3D 조립이 없다** — 최종 산출물은 파츠 세트이고 DepthOrder 의 유일한
    /// 소비처(SceneLayoutComposer 의 z 좌표 계산)는 배경 전용이다. 실 job 에서 겹치지
    /// 않는 파츠(어깨 갑주·귀걸이 등)에 전순서를 강제해 재시도를 소진시키고 있었다.
    /// </summary>
    [Fact]
    public void DuplicateDepthOrder_AllowedForCharacter()
    {
        var job = PipelineJob.Create(
            AssetCategory.Character, Guid.NewGuid(), Now, gender: Gender.Female);
        job.ApplyScene(TestScene.Default);
        job.ApplyParts(["몸통", "머리카락"]);

        job.ValidatePartDetails([Detail("몸통", depth: 1), Detail("머리카락", depth: 1)]);
    }

    /// <summary>
    /// 열거형 → 오류 코드 매핑.
    ///
    /// 위 테스트들은 `PartValidationError` 를 확인한다. 그것이 화면이 보는 문자열로
    /// 제대로 번역되는지는 별개 문제이고, **번역표가 틀리면 화면이 엉뚱한 안내를 한다.**
    /// 파이프라인을 통과시켜 실제로 나가는 값을 본다.
    /// </summary>
    // occludedBy 의 세트 밖 참조는 이제 task 실패 경로가 아니다 — 분해 해석이 버린다
    // (character-tuning workstream B, DecomposeReferenceLenienceTests). 도메인 레벨 거절은
    // OccludedByUnknownPart_Rejected 가 직접 검증한다
    [Theory]
    [InlineData("bounds", ErrorCode.PartBoundsOutOfRange)]
    [InlineData("reference", ErrorCode.PartNameMismatch)]
    [InlineData("depth", ErrorCode.PartDepthDuplicate)]
    [InlineData("duplicate", ErrorCode.PartDuplicate)]
    public async Task ValidationErrorsReachTheTaskAsErrorCodes(string flavor, string expected)
    {
        var decompose = flavor switch
        {
            "bounds" => Decompose(("P01", 2.0, 1, null), ("P02", 0.1, 2, null)),
            "reference" => Decompose(("P99", 0.1, 1, null), ("P02", 0.1, 2, null)),
            // 실제로 겪은 실패 — 같은 파츠 참조가 두 번 온 응답
            "duplicate" => Decompose(("P01", 0.1, 1, null), ("P01", 0.2, 2, null), ("P02", 0.3, 3, null)),
            _ => Decompose(("P01", 0.1, 1, null), ("P02", 0.1, 1, null)),
        };

        var fixture = new PipelineFixture(FakeLlmProvider.Returning(kind => kind switch
        {
            LlmOperationKind.Analyze => SceneJson,
            LlmOperationKind.Extract => """{"parts":["등대","부두"]}""",
            _ => decompose,
        }));

        var job = await fixture.StartJobAsync();
        await fixture.RunAllStagesAsync(job);
        // 분해는 계약 위반이라 한도까지 재시도된다 — 확정 실패를 보려면 소진시킨다
        await fixture.RunUntilTerminalAsync(job, TaskKind.Decompose);

        var task = job.Tasks.First(t => t.Kind == TaskKind.Decompose);
        Assert.Equal(expected, task.FailureReason);
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    private const string SceneJson = """
        {
          "palette": [
            { "name": "청회색 바다", "hex": "#2E5C6E" },
            { "name": "주황색 목재", "hex": "#D97A3C" },
            { "name": "옅은 안개", "hex": "#F2E8DC" }
          ],
          "timeOfDay": "저녁", "mood": "고요",
          "renderingStyle": "수채", "materialFeel": "목재",
          "camera": { "type": "one-point", "eyeLevel": "1.6m", "horizonY": 0.5 },
          "light": { "direction": "좌측", "temperature": "따뜻", "shadowHardness": "부드러움" },
          "scaleReference": { "object": "등대", "realWorldSize": "3m", "heightMeters": 3 }
        }
        """;

    private static string Decompose(params (string PartRef, double X, int Depth, string? Occluded)[] parts)
        => JsonSerializer.Serialize(new
        {
            parts = parts.Select(p => new
            {
                partRef = p.PartRef,
                category = "구조물",
                description = "서술",
                placements = new[] { new { x = p.X, y = 0.1, w = 0.2, h = 0.2 } },
                depthOrder = p.Depth,
                occludedBy = p.Occluded is null ? Array.Empty<string>() : [p.Occluded],
            }),
        });


    [Fact]
    public void ValidationDoesNotMutate_WhenItFails()
    {
        // 검증만 하고 반영하지 않는다는 것이 계약이다 —
        // 이것이 깨지면 실패한 공정에 결과가 반쯤 들어간다
        var job = JobWithParts();

        Assert.Throws<PartValidationException>(() =>
            job.ValidatePartDetails([Detail("등대", depth: 1), Detail("부두", depth: 1)]));

        Assert.All(job.Parts, p => Assert.Null(p.Description));
        Assert.All(job.Parts, p => Assert.Empty(p.Placements));
    }
}

/// <summary>파츠 분해의 모델용 참조 키 계약.</summary>
public sealed class DecomposeReferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PartReferences_ResolveToAuthoritativeNames()
    {
        // 실제 실패 재현 — 모델이 긴 파츠 이름을 가림 참조에서 축약해 작업이 실패했다
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        job.ApplyScene(TestScene.Default);
        job.ApplyParts([
            "절벽 사이로 이어지는 석조 아치 교량",
            "절벽 아래의 발광 청록색 수로와 폭포형 협곡",
        ]);
        var stage = new DecomposeStage();

        var variables = stage.BuildVariables(job);
        var commit = stage.Interpret(job, """
            {
              "parts": [
                {
                  "partRef": "P01", "category": "architecture", "description": "석조 교량",
                  "placements": [{ "x": 0.1, "y": 0.1, "w": 0.2, "h": 0.2 }],
                  "depthOrder": 1, "occludedBy": []
                },
                {
                  "partRef": "P02", "category": "water", "description": "청록색 수로",
                  "placements": [{ "x": 0.2, "y": 0.2, "w": 0.2, "h": 0.2 }],
                  "depthOrder": 2, "occludedBy": ["P01"]
                }
              ]
            }
            """);

        commit(job);

        Assert.Contains("P01: 절벽 사이로 이어지는 석조 아치 교량", variables["parts"]);
        Assert.Equal(
            ["절벽 사이로 이어지는 석조 아치 교량"],
            job.Parts[1].OccludedBy);
    }
}
