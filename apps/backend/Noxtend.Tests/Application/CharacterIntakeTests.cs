using Noxtend.Application.Job;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;

namespace Noxtend.Tests.Application;

/// <summary>
/// character-studio slice 2 — 캐릭터 성별 필수·힌트 범위 검증.
///
/// 성별 필수는 본질적으로 <c>if (category == Character)</c> 분기다. 이 분기를 접수
/// (`StartJobHandler`) 한 곳에만 두고 백본·스테이지로 새지 않게 한다 (§3.2 · NFR-04).
/// 타 카테고리는 성별 없이도 그대로 접수돼야 한다 (§7-3).
/// </summary>
public sealed class CharacterIntakeTests
{
    // 캐릭터인데 성별 없음 → 접수 거절, 작업도 큐도 없음
    [Fact]
    public async Task Character_WithoutGender_IsRejected()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None, gender: null);

        Assert.Equal(ErrorCode.JobGenderRequired, result.ErrorCode);
        Assert.Empty(fixture.Queue.Enqueued);
    }

    // 타 카테고리는 성별 없이도 접수된다 — 캐릭터 규칙이 배경을 깨지 않는다
    [Fact]
    public async Task NonCharacter_WithoutGender_IsAccepted()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None, gender: null);

        Assert.True(result.IsSuccess, result.ErrorCode);
    }

    // 힌트 개수가 범위 밖(0·21) → 거절
    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task PartHint_OutOfRange_IsRejected(int count)
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            gender: Gender.Female,
            partHints: [new PartHint("팔찌", count, null)]);

        Assert.Equal(ErrorCode.JobPartHintInvalid, result.ErrorCode);
        Assert.Empty(fixture.Queue.Enqueued);
    }

    // 힌트 Type 에 템플릿 구문({{ }})이 들어오면 거절된다 — 프롬프트 렌더 재스캔이 죽는 것을
    // 접수에서 막는다 (리뷰 #2). "{{scene}}" 같은 값이 partHints 변수로 렌더되면 Render 가
    // 잔여 {{...}} 로 오인해 InvalidOperationException 을 던져 캐릭터 작업이 통째로 실패한다
    [Theory]
    [InlineData("{{scene}}")]
    [InlineData("팔찌}}")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PartHint_WithInvalidType_IsRejected(string type)
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            gender: Gender.Female,
            partHints: [new PartHint(type, 3, null)]);

        Assert.Equal(ErrorCode.JobPartHintInvalid, result.ErrorCode);
        Assert.Empty(fixture.Queue.Enqueued);
    }

    // 변형(Variant)에 템플릿 구문이 들어와도 거절된다 — Type 과 같은 이유
    [Fact]
    public async Task PartHint_WithTemplateSyntaxInVariant_IsRejected()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            gender: Gender.Female,
            partHints: [new PartHint("팔찌", 3, "{{gender}}")]);

        Assert.Equal(ErrorCode.JobPartHintInvalid, result.ErrorCode);
    }

    // 경계 유효값(1·20)은 통과한다 — 범위 검증이 유효값까지 막지 않는지 확인
    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    public async Task PartHint_AtBoundary_IsAccepted(int count)
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            gender: Gender.Female,
            partHints: [new PartHint("팔찌", count, null)]);

        Assert.True(result.IsSuccess, result.ErrorCode);
    }

    // 성별·정상 힌트 → 접수 성공, 값이 도메인에 보존된다(힌트는 JSON 으로 굳음)
    [Fact]
    public async Task Character_WithGenderAndHints_IsAcceptedAndPersisted()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Character, uploadId, providerId, StubModelCatalog.DefaultModel,
            CancellationToken.None,
            gender: Gender.Female,
            partHints: [new PartHint("팔찌", 3, null), new PartHint("장갑", 2, "손목형")]);

        Assert.True(result.IsSuccess, result.ErrorCode);
        Assert.Equal(Gender.Female, result.Value!.Gender);
        Assert.NotNull(result.Value!.PartHints);   // JSON 으로 굳어 있다
        Assert.Contains("팔찌", result.Value!.PartHints);
    }
}
