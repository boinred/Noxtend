using Microsoft.EntityFrameworkCore;
using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Infrastructure.Persistence;
using Noxtend.Tuning.Domain.Call;

namespace Noxtend.Tests.Infrastructure;

/// <summary>
/// 마이그레이션을 **실제로 돌려** 단가 씨앗을 확인한다.
///
/// 씨앗 목록이 맞는 것과 그 목록이 DB 에 들어가는 것은 다른 일이다. 목록만 보는 검사는
/// 마이그레이션이 순회를 빠뜨리거나 유니크 인덱스에 걸려 배포가 통째로 실패하는 경우를
/// 못 잡는다 — 그 실패는 첫 배포에서야 드러난다.
///
/// InMemory 공급자로는 마이그레이션이 돌지 않아 컨테이너를 띄운다. 이미지는 개발용 k8s
/// 매니페스트(`deploy/k8s/mssql.yaml`)와 같은 태그로 맞춰 두 번 받지 않게 한다.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ModelPriceMigrationTests(SqlServerFixture sql)
{
    // xUnit 이 테스트마다 클래스를 새로 만든다 — 이 필드가 곧 "테스트당 DB 하나" 다.
    // `Context()` 안에서 뽑으면 한 테스트가 서로 다른 DB 를 보게 된다
    private readonly string connectionString = sql.FreshDatabase(nameof(ModelPriceMigrationTests));

    [Fact]
    public async Task MigratedDatabase_PricesCatalogImagesAndPreservesUnknownSpritePrice()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var book = new ModelPriceBook(await db.ModelPrices.ToListAsync());
        var at = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

        // 기존 모델 단가 보장과 확인하지 않은 sprite 단가의 null 유지
        var unpriced = Enum.GetValues<ProviderKind>()
            .SelectMany(ImageModels.For)
            .Select(model => model.Id)
            .Where(id => book.Estimate(id, at, 2_075, 1_377, images: 1) is null)
            .ToList();

        Assert.Equal(["gpt-image-2.5-sunburst"], unpriced);
        Assert.False(book.IsKnown("gpt-image-2.5-sunburst", at));
        Assert.Null(book.Estimate("gpt-image-2.5-sunburst", at, 2_075, 1_377, images: 1));
    }

    /// <summary>
    /// 3D 모델도 전부 비용이 나와야 한다 (사이클 #11).
    ///
    /// 단가가 비어 있으면 사용량 화면이 "미등록" 으로 남는다 — 이미지 쪽에서 실제로 겪은
    /// 일이고, 그때 36건이 값 없이 쌓였다.
    ///
    /// **파츠당 한 건이다.** 이미지 네 장을 올려도 유료 호출은 제출 하나뿐이라
    /// `images: 1` 로 센다.
    /// </summary>
    [Fact]
    public async Task MigratedDatabase_PricesEveryCatalogMeshModel()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var book = new ModelPriceBook(await db.ModelPrices.ToListAsync());
        var at = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);

        var unpriced = Enum.GetValues<ProviderKind>()
            .SelectMany(MeshModels.For)
            .Select(model => model.Id)
            .Where(id => book.Estimate(id, at, 0, 0, images: 1) is null)
            .Order()
            .ToList();

        // **이제 하나도 빠지지 않는다.** `meshy-7` 은 D-06 으로 미등록이었는데, 그 근거
        // ("크레딧당 USD 미공개")가 부정확했다 — API 문서에 없을 뿐 요금제에서 유도된다.
        // 요금제를 바꾸면 그날짜로 새 행을 넣으므로 지난 비용은 그대로 남는다.
        Assert.Empty(unpriced);
    }

    /// <summary>
    /// README "미구현·가라 목록" — 참조 이미지 장수별 실제 토큰 사용량이 비용 계산에
    /// 반영 안 되던 문제(B안: 블렌디드 근사 단가). `gpt-image-2`가 여전히 장당 정액만
    /// 쓰면 참조를 몇 장 보내든 같은 값이 나온다 — 토큰 단가가 실제로 반영됐는지 확인한다.
    /// </summary>
    [Fact]
    public async Task MigratedDatabase_PricesGptImage2ByTokensNotOnlyFlatRate()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        var book = new ModelPriceBook(await db.ModelPrices.ToListAsync());
        var at = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        // 참조 1장(입력 토큰 적음)과 참조 2장(입력 토큰 많음) 호출의 비용이 달라야
        // 실제 토큰이 계산에 쓰이고 있다는 뜻이다 — 장당 정액이면 둘이 같은 값을 낸다
        var oneReference = book.Estimate("gpt-image-2", at, inputTokens: 1500, outputTokens: 1120, images: 1);
        var twoReferences = book.Estimate("gpt-image-2", at, inputTokens: 2800, outputTokens: 1120, images: 1);

        Assert.NotNull(oneReference);
        Assert.NotNull(twoReferences);
        Assert.NotEqual(oneReference, twoReferences);
    }

    /// <summary>이 마이그레이션 바로 앞 — 여기까지 돌려 두고 운영자가 손을 댄 상황을 만든다.</summary>
    private const string BeforeImageCatalogPrices = "20260810034155_PartReferencePrompt";

    [Fact]
    public async Task Migration_KeepsThePriceAnOperatorAlreadyRegistered()
    {
        await using var db = Context();
        await db.Database.MigrateAsync(BeforeImageCatalogPrices);

        // 단가가 비어 있는 걸 발견한 운영자가 관리자 화면에서 먼저 등록한 행.
        // 씨앗과 같은 (모델, 시행일) 이라 `IX_ModelPrices_ModelEffectiveFrom` 에 부딪힌다
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ModelPrices] ([Id], [Model], [InputPerMillion], [OutputPerMillion],
                [EffectiveFrom], [Note], [PerImage])
            VALUES ({Guid.NewGuid()}, N'gemini-3.1-flash-lite-image', 0, 0,
                {SeededFrom}, N'운영자가 직접 등록', 0.09);
            """);

        await db.Database.MigrateAsync();

        // 배포가 살아남는 것으로 끝이 아니다 — 운영자가 넣은 값이 그대로여야 한다.
        // 씨앗이 덮으면 그 사람이 확인한 근거가 조용히 사라진다
        var rows = await db.ModelPrices
            .Where(p => p.Model == "gemini-3.1-flash-lite-image" && p.EffectiveFrom == SeededFrom)
            .ToListAsync();

        Assert.Equal(0.09m, Assert.Single(rows).PerImage);
    }

    [Fact]
    public async Task MetadataMigration_BackfillsExactKnownIdsAndPreservesUnclassifiedRows()
    {
        await using var db = Context();
        await db.Database.MigrateAsync("20261008040836_SeedSpriteSourcePrompt");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [ModelPrices] ([Id], [Model], [InputPerMillion], [OutputPerMillion],
                [EffectiveFrom], [Note])
            VALUES ({Guid.NewGuid()}, N'gpt-5-unknown-variant', 17, 23, {SeededFrom}, N'보존');
            """);
        await db.Database.MigrateAsync();

        var unknown = await db.ModelPrices.SingleAsync(p => p.Model == "gpt-5-unknown-variant");
        Assert.Null(unknown.Provider);
        Assert.True(unknown.AllowHistoricalFallback);
        Assert.Null(unknown.SourceEvidenceJson);
        Assert.Equal(17m, unknown.InputPerMillion);
        foreach (var (model, provider) in new[]
        {
            ("gpt-5", "openai"), ("claude-opus-4-5", "anthropic"),
            ("gemini-2.5-flash-image", "google"), ("P1-20260311", "tripo"), ("meshy-7", "meshy")
        })
        {
            var row = await db.ModelPrices.FirstAsync(p => p.Model == model);
            Assert.Equal(provider, row.Provider);
            Assert.True(row.AllowHistoricalFallback);
            Assert.Null(row.SourceEvidenceJson);
        }
    }

    [Fact]
    public async Task CollectedMetadata_RoundTripsWithoutHistoricalFallback()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();
        var row = ModelPrice.CreateCollected("new-model", 2m, 3m, null, null, null,
            SeededFrom, "근거", null, "openai", "[{\"sha256\":\"test\"}]");
        db.ModelPrices.Add(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var saved = await db.ModelPrices.SingleAsync(p => p.Id == row.Id);
        Assert.Equal("openai", saved.Provider);
        Assert.False(saved.AllowHistoricalFallback);
        Assert.Equal(row.SourceEvidenceJson, saved.SourceEvidenceJson);
        Assert.Null(new ModelPriceBook([saved]).Estimate("new-model", SeededFrom.AddDays(-1), 1, 0));
    }

    private static readonly DateTimeOffset SeededFrom =
        new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private NoxtendDbContext Context()
        => new(new DbContextOptionsBuilder<NoxtendDbContext>()
            .UseSqlServer(connectionString)
            .Options);
}
