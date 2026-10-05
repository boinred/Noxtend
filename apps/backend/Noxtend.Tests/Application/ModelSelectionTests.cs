using Noxtend.Application.Common;
using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Ports;

namespace Noxtend.Tests.Application;

/// <summary>
/// 접수 시점의 모델 검증.
///
/// Design Ref: §4.2 #5 · §4.2 #14 (신설)
///
/// 스튜디오가 목록에서 고른 값을 보내지만, 오래된 탭은 사라진 모델 id 를 보낼 수 있다.
/// 워커까지 가서 실패하면 사용자에게는 "알 수 없는 이유로 실패" 로 보인다.
/// </summary>
public sealed class ModelSelectionTests
{
    [Fact]
    public async Task Start_RecordsTheChosenModelOnTheTask()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, "claude-sonnet-5", CancellationToken.None);

        Assert.True(result.IsSuccess);
        // 세 공정이 같은 모델을 쓴다 (Plan D-11)
        Assert.All(result.Value!.Tasks, t => Assert.Equal("claude-sonnet-5", t.Model));
    }

    [Fact]
    public async Task Start_RejectsModelThatIsNotInTheCatalog()
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, "gpt-4o", CancellationToken.None);

        Assert.Equal(ErrorCode.JobModelUnavailable, result.ErrorCode);
        // 작업이 만들어지지도, 큐에 들어가지도 않는다
        Assert.Empty(fixture.Queue.Enqueued);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_RejectsMissingModel(string model)
    {
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, model, CancellationToken.None);

        Assert.Equal(ErrorCode.JobModelUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task Start_SurfacesCatalogOutageAsProviderCallFailed()
    {
        // 목록을 못 가져오면 접수가 막힌다 (승인된 실패 경로 — 자유 입력 폴백 없음).
        // 오류 코드가 구별되어야 화면이 "연결 확인부터" 를 안내할 수 있다
        var fixture = new PipelineFixture();
        var uploadId = await fixture.SeedUploadAsync();
        var providerId = await fixture.SeedProviderAsync();
        fixture.Catalog.Failure = new ProviderCallFailedException("HTTP 401");

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, uploadId, providerId, "claude-opus-5", CancellationToken.None);

        Assert.Equal(ErrorCode.ProviderCallFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Start_ChecksUploadAndProviderBeforeTheCatalog()
    {
        // 순서가 중요하다. 업로드가 없는데 카탈로그를 먼저 부르면 공급자 API 를
        // 헛되게 두드리고, 사용자는 엉뚱한 오류를 본다
        var fixture = new PipelineFixture();
        var providerId = await fixture.SeedProviderAsync();
        fixture.Catalog.Failure = new ProviderCallFailedException("불려서는 안 된다");

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, Guid.NewGuid(), providerId, "claude-opus-5",
            CancellationToken.None);

        Assert.Equal(ErrorCode.JobUploadNotFound, result.ErrorCode);
    }
}
