using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Noxtend.Api.Contracts;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Llm;
using Noxtend.Domain.Ports;
using Noxtend.Infrastructure.Llm;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tuning.Application.Prompts;
using Noxtend.Tuning.Domain.Prompt;

namespace Noxtend.Tests.Application;

/// <summary>
/// Design Ref: §8.1 #11~#13 — LLM 호출 내역.
///
/// **Check 단계에서 이 테스트들이 없다는 것이 드러났다** (G-3). 특히 #12 는
/// 데코레이터를 도입한 **유일한 이유**인데 검증 코드가 없어 "그렇게 짰다" 는 주장뿐이었다.
/// </summary>
public sealed class LlmCallRecordingTests
{
    private static readonly LlmCallContext Context = LlmCallContext.ForTask(
        Guid.NewGuid(), Guid.NewGuid(), TaskKind.Analyze,
        Guid.NewGuid(), Guid.NewGuid(), "claude-opus-5");

    private static LlmRequest Request => new(Context, "시스템 프롬프트", "사용자 프롬프트", [], "{}");

    private static (RecordingLlmProvider Provider, InMemoryLlmCallRepository Calls)
        Build(ILlmProvider inner, Exception? recorderFailure = null)
    {
        var calls = new InMemoryLlmCallRepository { Failure = recorderFailure };
        var recorder = new TuningLlmCallRecorder(calls, FixedClock.Default);

        return (new RecordingLlmProvider(inner, recorder, NullLogger<RecordingLlmProvider>.Instance), calls);
    }

    // §8.1 #11 — 성공 기록
    [Fact]
    public async Task RecordsSuccessfulCall()
    {
        var (provider, calls) = Build(FakeLlmProvider.Returning(_ => """{"ok":true}"""));

        await provider.CompleteAsync(Request, CancellationToken.None);

        var call = Assert.Single(calls.All);
        Assert.True(call.Succeeded);
        Assert.Equal("""{"ok":true}""", call.ResponsePayload);
        Assert.Contains("시스템 프롬프트", call.RequestPayload);
    }

    // §8.1 #11 — 실패 기록. 사이클 #4 에서 "코드만 남고 원인을 모르는" 상황을 겪었다
    [Fact]
    public async Task RecordsFailedCall()
    {
        var (provider, calls) = Build(
            FakeLlmProvider.Failing(new ProviderCallFailedException("HTTP 401")));

        await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.CompleteAsync(Request, CancellationToken.None));

        var call = Assert.Single(calls.All);
        Assert.False(call.Succeeded);
        Assert.Equal("HTTP 401", call.FailureReason);
        Assert.Null(call.ResponsePayload);
    }

    [Fact]
    public async Task DoesNotRecordCancellation()
    {
        // 취소는 실패가 아니다. 사용자의 결정이므로 실패로 기록하면 통계가 왜곡된다
        var (provider, calls) = Build(FakeLlmProvider.Succeeding().WithDelay(TimeSpan.FromSeconds(5)));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CompleteAsync(Request, cts.Token));

        Assert.Empty(calls.All);
    }

    // §8.1 #12 — **G-4. 데코레이터를 도입한 유일한 이유다**
    [Fact]
    public async Task CallSucceeds_EvenWhenRecordingThrows()
    {
        var (provider, _) = Build(
            FakeLlmProvider.Returning(_ => """{"ok":true}"""),
            recorderFailure: new InvalidOperationException("DB 연결 끊김"));

        var result = await provider.CompleteAsync(Request, CancellationToken.None);

        // 관측을 위해 넣은 장치가 시스템을 더 약하게 만들면 안 된다
        Assert.Equal("""{"ok":true}""", result.RawJson);
    }

    [Fact]
    public async Task OriginalFailurePropagates_EvenWhenRecordingThrows()
    {
        // 기록 실패가 원래 실패를 가리면 진단이 두 배로 어려워진다
        var (provider, _) = Build(
            FakeLlmProvider.Failing(new ProviderCallFailedException("HTTP 500")),
            recorderFailure: new InvalidOperationException("DB 연결 끊김"));

        var ex = await Assert.ThrowsAsync<ProviderCallFailedException>(
            () => provider.CompleteAsync(Request, CancellationToken.None));

        Assert.Equal("HTTP 500", ex.Message);
    }

    // §8.1 #13 — 재현성의 핵심
    [Fact]
    public async Task RecordsPromptVersionId()
    {
        var (provider, calls) = Build(FakeLlmProvider.Succeeding());

        await provider.CompleteAsync(Request, CancellationToken.None);

        Assert.Equal(Context.PromptVersionId, calls.All[0].PromptVersionId);
        Assert.NotEqual(Guid.Empty, calls.All[0].PromptVersionId);
    }

    [Fact]
    public async Task RequestPayloadCarriesNoImageBytes()
    {
        // 이미지는 StoredImage 에 이미 있다. 여기 또 넣으면 표가 수십 배로 부푼다 (§7 S-3)
        var (provider, calls) = Build(FakeLlmProvider.Succeeding());
        var withImage = Request with { Images = [new LlmImage("original", new ImageContent([1, 2, 3, 4], "image/png"))] };

        await provider.CompleteAsync(withImage, CancellationToken.None);

        Assert.DoesNotContain("AQIDBA", calls.All[0].RequestPayload);   // base64 of 1,2,3,4
    }
}

/// <summary>
/// Design Ref: §8.1 #14~#15 — 프롬프트 버전 규칙.
///
/// 런타임으로는 확인했지만 회귀를 막지 못했다 (G-3).
/// </summary>
public sealed class PromptVersioningTests
{
    private sealed class Fixture
    {
        public InMemoryPromptVersionRepository Prompts { get; } = new();
        public FixedClock Clock { get; } = FixedClock.Default;

        public CreatePromptVersionHandler Create => new(Prompts, Clock);
        public ActivatePromptVersionHandler Activate => new(Prompts);

        public async Task<Guid> SeedAsync(LlmOperationKind kind, bool active, AssetCategory? category = null)
        {
            var result = await Create.HandleAsync(
                kind, category, kind == LlmOperationKind.Analyze ? "프롬프트" : "프롬프트 {{scene}}",
                "user", "{}", null, CancellationToken.None);

            if (active)
            {
                await Activate.HandleAsync(result.Value!.Id, CancellationToken.None);
            }

            return result.Value!.Id;
        }
    }

    // prompt-category-axis §8-1 — 카테고리 보존
    [Fact]
    public void CreatePreservesCategory()
    {
        var created = PromptVersion.Create(
            LlmOperationKind.Extract, AssetCategory.Character, 1, "system", "user", "{}", null,
            FixedClock.Default.Now);

        Assert.Equal(AssetCategory.Character, created.Category);
    }

    // prompt-category-axis §4.1 — 없음(null)이 기본이다
    [Fact]
    public void CategoryIsNullWhenOmitted()
    {
        var created = PromptVersion.Create(
            LlmOperationKind.Extract, null, 1, "system", "user", "{}", null, FixedClock.Default.Now);

        Assert.Null(created.Category);
    }

    // §8.1 #14 — 저장이 활성화가 아니다
    [Fact]
    public async Task NewVersionIsInactive()
    {
        var fixture = new Fixture();

        var result = await fixture.Create.HandleAsync(
            LlmOperationKind.Analyze, null, "프롬프트", "user", "{}", "메모", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Equal(1, result.Value.Version);
    }

    [Fact]
    public async Task VersionNumbersIncrementPerKind()
    {
        // 전역 일련번호가 아니다 — 단계마다 1부터
        var fixture = new Fixture();

        await fixture.Create.HandleAsync(LlmOperationKind.Analyze, null, "a", "u", "{}", null, CancellationToken.None);
        var second = await fixture.Create.HandleAsync(LlmOperationKind.Analyze, null, "b", "u", "{}", null, CancellationToken.None);
        var other = await fixture.Create.HandleAsync(LlmOperationKind.Extract, null, "{{scene}}", "u", "{}", null, CancellationToken.None);

        Assert.Equal(2, second.Value!.Version);
        Assert.Equal(1, other.Value!.Version);
    }

    // §8.1 #15 — 활성 전환
    [Fact]
    public async Task ActivatingDeactivatesThePrevious()
    {
        var fixture = new Fixture();
        await fixture.SeedAsync(LlmOperationKind.Analyze, active: true);
        var second = await fixture.SeedAsync(LlmOperationKind.Analyze, active: false);

        await fixture.Activate.HandleAsync(second, CancellationToken.None);

        var active = await fixture.Prompts.ListActiveAsync(CancellationToken.None);
        var single = Assert.Single(active);
        Assert.Equal(second, single.Id);
    }

    [Fact]
    public async Task RollbackIsJustActivatingAnOlderVersion()
    {
        var fixture = new Fixture();
        var first = await fixture.SeedAsync(LlmOperationKind.Analyze, active: true);
        var second = await fixture.SeedAsync(LlmOperationKind.Analyze, active: false);
        await fixture.Activate.HandleAsync(second, CancellationToken.None);

        await fixture.Activate.HandleAsync(first, CancellationToken.None);

        var active = Assert.Single(await fixture.Prompts.ListActiveAsync(CancellationToken.None));
        Assert.Equal(first, active.Id);
        Assert.Equal(1, active.Version);
    }

    [Fact]
    public async Task ActivatingTheActiveVersionIsIdempotent()
    {
        var fixture = new Fixture();
        var id = await fixture.SeedAsync(LlmOperationKind.Analyze, active: true);

        var result = await fixture.Activate.HandleAsync(id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await fixture.Prompts.ListActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RejectsUnknownVariableAtSaveTime()
    {
        // 활성화한 뒤 그 단계의 모든 실행이 실패하는 것보다 낫다 (§2.3-5)
        var fixture = new Fixture();

        var result = await fixture.Create.HandleAsync(
            LlmOperationKind.Extract, null, "{{scen}} 오타", "user", "{}", null, CancellationToken.None);

        Assert.Equal(ErrorCode.PromptUnknownVariable, result.ErrorCode);
    }

    [Fact]
    public async Task RejectsInvalidJsonSchema()
    {
        var fixture = new Fixture();

        var result = await fixture.Create.HandleAsync(
            LlmOperationKind.Analyze, null, "프롬프트", "user", "{ 이건 JSON 이 아니다", null, CancellationToken.None);

        Assert.Equal(ErrorCode.PromptSchemaInvalid, result.ErrorCode);
    }
}

/// <summary>
/// prompt-category-axis §7 · §8-11 · slice 6 — API 계약이 카테고리를 나른다.
///
/// 생성이 카테고리를 저장하고, 응답이 asset-category-contract 표기로 실으며,
/// 이력 조회가 카테고리로 정확히 걸린다.
/// </summary>
public sealed class PromptCategoryApiContractTests
{
    private static readonly DateTimeOffset Now = FixedClock.Default.Now;

    private static (CreatePromptVersionHandler Create, ListPromptVersionsHandler List) Handlers(
        out InMemoryPromptVersionRepository repo)
    {
        repo = new InMemoryPromptVersionRepository();
        return (new CreatePromptVersionHandler(repo, FixedClock.Default), new ListPromptVersionsHandler(repo));
    }

    // 생성이 카테고리를 저장한다
    [Fact]
    public async Task Create_PersistsCategory()
    {
        var (create, _) = Handlers(out _);

        var result = await create.HandleAsync(
            LlmOperationKind.Extract, AssetCategory.Character, "{{scene}}", "user", "{}", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssetCategory.Character, result.Value!.Category);
        Assert.False(result.Value.IsActive);   // 저장이 활성화가 아니다(기존 성질 유지)
    }

    // 응답이 카테고리를 character|object|background 표기로 싣는다
    [Fact]
    public void Response_WiresCategoryString()
    {
        var character = PromptVersion.Create(
            LlmOperationKind.Extract, AssetCategory.Character, 1, "system", "user", "{}", null, Now);
        var background = PromptVersion.Create(
            LlmOperationKind.Extract, null, 1, "system", "user", "{}", null, Now);

        Assert.Equal("character", PromptVersionResponse.From(character).Category);
        Assert.Null(PromptVersionResponse.From(background).Category);   // 기본은 null
    }

    // 이력 조회가 카테고리로 정확히 걸린다 — 기본과 전용이 안 섞인다
    [Fact]
    public async Task List_FiltersByCategoryExactly()
    {
        var (create, list) = Handlers(out _);
        await create.HandleAsync(LlmOperationKind.Extract, null, "{{scene}}", "u", "{}", null, CancellationToken.None);
        await create.HandleAsync(
            LlmOperationKind.Extract, AssetCategory.Character, "{{scene}}", "u", "{}", null, CancellationToken.None);

        var characterHistory = await list.HandleAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);
        var defaultHistory = await list.HandleAsync(LlmOperationKind.Extract, null, CancellationToken.None);

        Assert.Equal(AssetCategory.Character, Assert.Single(characterHistory).Category);
        Assert.Null(Assert.Single(defaultHistory).Category);
    }
}

/// <summary>
/// prompt-category-axis §6.1 · §8-8/8b · slice 4 — 활성 전환 격리(양방향).
///
/// **이 사이클의 회귀 방지선이다.** 한 카테고리를 켤 때 다른 카테고리가 조용히
/// 꺼지면 사람이 알아챌 수단이 없다. 활성 전환은 같은 (Kind, Category) 의 활성만 내린다.
/// </summary>
public sealed class PromptActivationIsolationTests
{
    private static readonly DateTimeOffset Now = FixedClock.Default.Now;

    private static PromptVersion Make(AssetCategory? category, int version, bool active)
    {
        var v = PromptVersion.Create(LlmOperationKind.Extract, category, version, "system", "user", "{}", null, Now);
        if (active)
        {
            v.Activate();
        }

        return v;
    }

    private static async Task<InMemoryPromptVersionRepository> RepoAsync(params PromptVersion[] rows)
    {
        var repo = new InMemoryPromptVersionRepository();
        foreach (var row in rows)
        {
            await repo.AddAsync(row, CancellationToken.None);
        }

        await repo.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    // §8-8 캐릭터를 켜도 기본(배경) 활성이 유지된다
    [Fact]
    public async Task ActivatingCharacter_KeepsDefaultActive()
    {
        var background = Make(null, 1, active: true);
        var character = Make(AssetCategory.Character, 1, active: false);
        var repo = await RepoAsync(background, character);

        await new ActivatePromptVersionHandler(repo).HandleAsync(character.Id, CancellationToken.None);

        var stillDefault = await repo.GetActiveAsync(LlmOperationKind.Extract, null, CancellationToken.None);
        var nowCharacter = await repo.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.Equal(background.Id, stillDefault!.Id);   // 배경 회귀 없음
        Assert.Equal(character.Id, nowCharacter!.Id);
    }

    // §8-8b 기본 새 버전을 켜도 캐릭터 활성이 유지된다(역방향)
    [Fact]
    public async Task ActivatingDefault_KeepsCharacterActive()
    {
        var character = Make(AssetCategory.Character, 1, active: true);
        var backgroundV1 = Make(null, 1, active: true);
        var backgroundV2 = Make(null, 2, active: false);
        var repo = await RepoAsync(character, backgroundV1, backgroundV2);

        await new ActivatePromptVersionHandler(repo).HandleAsync(backgroundV2.Id, CancellationToken.None);

        var stillCharacter = await repo.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);
        var nowDefault = await repo.GetActiveAsync(LlmOperationKind.Extract, null, CancellationToken.None);

        Assert.Equal(character.Id, stillCharacter!.Id);   // 캐릭터 회귀 없음
        Assert.Equal(backgroundV2.Id, nowDefault!.Id);
    }
}

/// <summary>
/// prompt-category-axis §3.1 · §8-2~4 · slice 3 — 어댑터 폴백 2단.
///
/// 전용이 있으면 전용, 없으면 기본(null). 규칙이 사는 유일한 자리가 여기다 —
/// 가짜 리포지토리만으로 DB 없이 고정한다.
/// </summary>
public sealed class PromptFallbackTests
{
    private static readonly DateTimeOffset Now = FixedClock.Default.Now;

    private static async Task<TuningPromptCatalog> BuildAsync(
        params (AssetCategory? Category, int Version)[] activeExtractRows)
    {
        var repo = new InMemoryPromptVersionRepository();
        foreach (var (category, version) in activeExtractRows)
        {
            var v = PromptVersion.Create(LlmOperationKind.Extract, category, version, "system", "user", "{}", null, Now);
            v.Activate();
            await repo.AddAsync(v, CancellationToken.None);
        }

        await repo.SaveChangesAsync(CancellationToken.None);
        return new TuningPromptCatalog(repo);
    }

    // §8-2 전용·기본이 둘 다 활성이면 전용이 나온다
    [Fact]
    public async Task PrefersDedicatedOverDefault()
    {
        var catalog = await BuildAsync((null, 1), (AssetCategory.Character, 1));

        var snapshot = await catalog.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.Equal("system", snapshot!.System);
        Assert.Equal(1, snapshot.Version);
    }

    // §8-3 전용이 없으면 기본으로 폴백한다 — 배경 회귀 없음의 핵심
    [Fact]
    public async Task FallsBackToDefaultWhenNoDedicated()
    {
        var catalog = await BuildAsync((null, 7));

        var snapshot = await catalog.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(7, snapshot!.Version);
    }

    // §8-4 둘 다 없으면 null → 호출자가 PROMPT_NOT_ACTIVE
    [Fact]
    public async Task ReturnsNullWhenNeitherActive()
    {
        var catalog = await BuildAsync();

        var snapshot = await catalog.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.Null(snapshot);
    }
}

/// <summary>
/// prompt-category-axis §5.3 · slice 2 — 리포지토리는 정확 일치만 안다.
///
/// 폴백은 어댑터가 하고(slice 3), 리포지토리는 (Kind, Category) 정확 일치·채번만 한다.
/// InMemory 가 DB 의 (Kind, Category) 필터 유니크를 흉내내는지도 여기서 고정한다 —
/// 안 그러면 격리 테스트(slice 4)가 구현이 옳아도 실패한다.
/// </summary>
public sealed class PromptCategoryRepositoryTests
{
    private static readonly DateTimeOffset Now = FixedClock.Default.Now;

    private static async Task<InMemoryPromptVersionRepository> SeedActiveAsync(
        params (LlmOperationKind Kind, AssetCategory? Category, int Version)[] rows)
    {
        var repo = new InMemoryPromptVersionRepository();
        foreach (var (kind, category, version) in rows)
        {
            var v = PromptVersion.Create(kind, category, version, "system", "user", "{}", null, Now);
            v.Activate();
            await repo.AddAsync(v, CancellationToken.None);
        }

        await repo.SaveChangesAsync(CancellationToken.None);
        return repo;
    }

    // 정확 일치 — 전용과 기본이 둘 다 활성일 때 각각을 따로 집는다
    [Fact]
    public async Task GetActive_MatchesCategoryExactly()
    {
        var repo = await SeedActiveAsync(
            (LlmOperationKind.Extract, null, 1),
            (LlmOperationKind.Extract, AssetCategory.Character, 1));

        var character = await repo.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);
        var fallback = await repo.GetActiveAsync(LlmOperationKind.Extract, null, CancellationToken.None);

        Assert.Equal(AssetCategory.Character, character!.Category);
        Assert.Null(fallback!.Category);
    }

    // 전용이 없으면 정확 일치는 null — 폴백하지 않는다(폴백은 어댑터)
    [Fact]
    public async Task GetActive_ExactMissReturnsNull_NoFallback()
    {
        var repo = await SeedActiveAsync((LlmOperationKind.Extract, null, 1));

        var character = await repo.GetActiveAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.Null(character);
    }

    // 채번은 (Kind, Category) 스코프 — 카테고리마다 1부터
    [Fact]
    public async Task NextVersion_CountsPerCategory()
    {
        var repo = new InMemoryPromptVersionRepository();
        foreach (var version in new[] { 1, 2 })
        {
            await repo.AddAsync(
                PromptVersion.Create(LlmOperationKind.Extract, null, version, "s", "u", "{}", null, Now),
                CancellationToken.None);
        }

        var nextDefault = await repo.NextVersionAsync(LlmOperationKind.Extract, null, CancellationToken.None);
        var nextCharacter = await repo.NextVersionAsync(LlmOperationKind.Extract, AssetCategory.Character, CancellationToken.None);

        Assert.Equal(3, nextDefault);
        Assert.Equal(1, nextCharacter);
    }

    // 유일성은 {Kind, Category} — 다른 카테고리 활성은 공존한다
    [Fact]
    public async Task ActiveUniqueness_IsScopedByKindAndCategory()
    {
        var ex = await Record.ExceptionAsync(() => SeedActiveAsync(
            (LlmOperationKind.Extract, null, 1),
            (LlmOperationKind.Extract, AssetCategory.Character, 1)));

        Assert.Null(ex); // 서로 다른 카테고리 — 공존 허용

        // 같은 (Kind, Category) 활성 둘은 거부
        await Assert.ThrowsAsync<InvalidOperationException>(() => SeedActiveAsync(
            (LlmOperationKind.Extract, AssetCategory.Character, 1),
            (LlmOperationKind.Extract, AssetCategory.Character, 2)));
    }
}

/// <summary>
/// Design Ref: §8.1 #19 · §7 S-1·S-2 · QC-01 — 응답 DTO 에 키가 없다.
///
/// 사이클 #4 의 `ProviderResponse` 검사와 같은 방식이다. 인증이 없는 동안 이것이
/// 유일한 자동 방어선이라는 사정도 같다.
/// </summary>
public sealed class TuningResponseSecurityTests
{
    private const string PlainKey = "sk-ant-secret-value-4f2c";

    [Fact]
    public void LlmCallResponse_ExposesOnlyTheAgreedFields()
    {
        // 키를 나르는 속성이 추가되면 여기서 걸린다. 값 검사와 달리 **모양**을 고정한다
        var actual = typeof(LlmCallResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        string[] expected =
        [
            "At", "EstimatedCostUsd", "FailureReason", "Id", "InputTokens", "Kind",
            "LatencyMs", "Model",
            // 사이클 #7 — 장 수. 이미지 호출은 토큰 칸이 비므로 이 값이 과금의 곱수다 (§3.3)
            "OutputImages",
            "OutputTokens", "PromptVersionId", "RequestPayload", "ResponsePayload",
            // background-similarity-tuning §7.3 — 평가 호출의 상관관계. 비용 화면이 잇는다
            "SimilarityEvaluationId",
            "Succeeded", "TaskId",
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PromptVersionResponse_CarriesNoCredentials()
    {
        var actual = typeof(PromptVersionResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain(actual, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(actual, n => n.Contains("Cipher", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(actual, n => n.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RequestPayload_CannotCarryTheApiKey()
    {
        // 구조적 보장 확인: 데코레이터는 LlmRequest 만 본다. 키는 어댑터 생성자 안에만
        // 있으므로 여기 들어올 경로가 없다 (§7 S-1)
        var request = new LlmRequest(
            LlmCallContext.ForTask(Guid.NewGuid(), Guid.NewGuid(), TaskKind.Analyze,
                Guid.NewGuid(), Guid.NewGuid(), "claude-opus-5"),
            "시스템", "사용자", [], "{}");

        var serialized = JsonSerializer.Serialize(request);

        Assert.DoesNotContain(PlainKey, serialized);
        // LlmRequest 에 키를 담을 필드 자체가 없다는 것이 요점이다
        Assert.DoesNotContain("apiKey", serialized, StringComparison.OrdinalIgnoreCase);
    }
}
