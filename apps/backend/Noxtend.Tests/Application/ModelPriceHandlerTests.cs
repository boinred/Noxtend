using Noxtend.Domain.Common;
using Noxtend.Infrastructure.Persistence.InMemory;
using Noxtend.Tuning.Application.Prices;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Application;

/// <summary>
/// 단가 편집의 경계.
///
/// 화면에서 고칠 수 있게 되면 **잘못된 값이 들어올 경로가 생긴다.** 그전에는 코드
/// 리뷰가 걸러줬지만 이제는 핸들러가 마지막 방어선이다.
/// </summary>
public sealed class ModelPriceHandlerTests
{
    private static readonly DateTimeOffset Aug = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static ModelPriceInput Input(
        string model = "gpt-5.6-luna",
        decimal input = 0.20m,
        DateTimeOffset? from = null,
        int? longFrom = null,
        decimal? longInput = null,
        decimal? longOutput = null)
        => new(model, input, 1.20m, longFrom, longInput, longOutput, from ?? Aug, "테스트");

    [Fact]
    public async Task ManualCreate_DefaultsToLegacyAndAcceptsOptionalProvider()
    {
        var repo = new InMemoryModelPriceRepository();
        var legacy = await new CreateModelPriceHandler(repo).HandleAsync(Input(), default);
        Assert.Null(legacy.Value!.Provider);
        Assert.True(legacy.Value.AllowHistoricalFallback);
        Assert.Null(legacy.Value.SourceEvidenceJson);
        var classified = await new CreateModelPriceHandler(repo)
            .HandleAsync(Input(from: Aug.AddDays(1)) with { Provider = "openai" }, default);
        Assert.Equal("openai", classified.Value!.Provider);
        var invalid = await new CreateModelPriceHandler(repo)
            .HandleAsync(Input(from: Aug.AddDays(2)) with { Provider = "unknown" }, default);
        Assert.Equal(ErrorCode.PriceInvalid, invalid.ErrorCode);
        Assert.Equal(2, (await repo.ListAsync(default)).Count);
    }

    [Fact]
    public async Task LegacyUpdate_PreservesCollectedServerMetadata()
    {
        var repo = new InMemoryModelPriceRepository();
        var price = ModelPrice.CreateCollected("model", 1m, 2m, null, null, null,
            Aug, "수집", null, "openai", "[]");
        await repo.AddAsync(price, default);
        var result = await new UpdateModelPriceHandler(repo).HandleAsync(price.Id, Input(), default);
        Assert.True(result.IsSuccess);
        Assert.Equal("openai", result.Value!.Provider);
        Assert.False(result.Value.AllowHistoricalFallback);
        Assert.Equal("[]", result.Value.SourceEvidenceJson);
    }

    [Fact]
    public async Task Create_AddsRow()
    {
        var repo = new InMemoryModelPriceRepository();

        var result = await new CreateModelPriceHandler(repo).HandleAsync(Input(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(await repo.ListAsync(default));
    }

    /// <summary>
    /// **인상은 새 행이다.** 같은 모델에 시행일만 다른 행이 쌓이는 것이 정상이고,
    /// 이것이 막히면 과거 지출을 보존할 방법이 없어진다.
    /// </summary>
    [Fact]
    public async Task Create_AllowsSameModelWithDifferentEffectiveDate()
    {
        var repo = new InMemoryModelPriceRepository();
        var handler = new CreateModelPriceHandler(repo);

        await handler.HandleAsync(Input(from: Aug), default);
        var second = await handler.HandleAsync(Input(input: 0.25m, from: Aug.AddMonths(1)), default);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, (await repo.ListAsync(default)).Count);
    }

    [Fact]
    public async Task Create_RejectsSameModelAndDate()
    {
        // 둘이면 어느 쪽이 이길지 알 수 없다 — DB 유니크 인덱스와 같은 규칙이다
        var repo = new InMemoryModelPriceRepository();
        var handler = new CreateModelPriceHandler(repo);

        await handler.HandleAsync(Input(), default);
        var duplicate = await handler.HandleAsync(Input(input: 0.25m), default);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal(ErrorCode.PriceDuplicate, duplicate.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsHalfFilledLongTier()
    {
        // 반쪽 행은 조용히 짧은 단가로 계산되어 실제보다 싼 값을 낸다
        var repo = new InMemoryModelPriceRepository();

        var result = await new CreateModelPriceHandler(repo)
            .HandleAsync(Input(longFrom: 272_000, longInput: 0.40m), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PriceInvalid, result.ErrorCode);
        Assert.Empty(await repo.ListAsync(default));
    }

    [Fact]
    public async Task Update_ChangesTheRow()
    {
        var repo = new InMemoryModelPriceRepository();
        var created = await new CreateModelPriceHandler(repo).HandleAsync(Input(), default);

        var result = await new UpdateModelPriceHandler(repo)
            .HandleAsync(created.Value!.Id, Input(input: 0.30m), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.30m, result.Value!.InputPerMillion);
    }

    [Fact]
    public async Task Update_RejectsCollidingEffectiveDate()
    {
        var repo = new InMemoryModelPriceRepository();
        var handler = new CreateModelPriceHandler(repo);

        await handler.HandleAsync(Input(from: Aug), default);
        var second = await handler.HandleAsync(Input(from: Aug.AddMonths(1)), default);

        // 시행일을 첫 행과 같은 날로 옮기면 두 행이 겹친다
        var result = await new UpdateModelPriceHandler(repo)
            .HandleAsync(second.Value!.Id, Input(from: Aug), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PriceDuplicate, result.ErrorCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFoundForUnknownId()
    {
        var result = await new UpdateModelPriceHandler(new InMemoryModelPriceRepository())
            .HandleAsync(Guid.NewGuid(), Input(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.PriceNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        var repo = new InMemoryModelPriceRepository();
        var created = await new CreateModelPriceHandler(repo).HandleAsync(Input(), default);

        var result = await new DeleteModelPriceHandler(repo).HandleAsync(created.Value!.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(await repo.ListAsync(default));
    }
}
