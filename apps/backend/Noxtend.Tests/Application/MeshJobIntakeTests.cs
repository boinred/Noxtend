using Noxtend.Domain.Common;
using Noxtend.Domain.Job;
using Noxtend.Domain.Provider;

namespace Noxtend.Tests.Application;

/// <summary>
/// 3D 선택이 접수를 어떻게 통과하는가.
///
/// Design Ref: §10.2 · Plan FR-15
///
/// **틀린 조합을 접수 시점에 막는 것이 요지다.** 받아 두면 사용자는 텍스트 세 단계와
/// 이미지 생성을 다 치르고 나서야 3D 가 아무것도 만들지 않았음을 알게 된다.
/// </summary>
public sealed class MeshJobIntakeTests
{
    [Fact]
    public async Task NoMeshSelection_StillAccepts()
    {
        var (fixture, upload, text, image) = await ReadyAsync();

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            image, StubModelCatalog.DefaultImageModel);

        // 3D 없이 접수된 작업은 예전처럼 이미지까지만 돈다 (NFR-06)
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.ProducesMeshes);
    }

    [Fact]
    public async Task MeshSelection_IsRemembered()
    {
        var (fixture, upload, text, image) = await ReadyAsync();
        var mesh = await SeedMeshProviderAsync(fixture);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            image, StubModelCatalog.DefaultImageModel,
            mesh, StubModelCatalog.DefaultMeshModel);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.ProducesMeshes);
        Assert.Equal(StubModelCatalog.DefaultMeshModel, result.Value.MeshModel);
    }

    /// <summary>
    /// 공급자만 고르고 모델을 못 불러온 화면이 그대로 보내면, 작업은 3D 를 만들 것처럼
    /// 보이면서 아무것도 만들지 않는다.
    /// </summary>
    [Fact]
    public async Task MeshProviderWithoutAModel_IsRejected()
    {
        var (fixture, upload, text, image) = await ReadyAsync();
        var mesh = await SeedMeshProviderAsync(fixture);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            image, StubModelCatalog.DefaultImageModel,
            mesh, null);

        Assert.Equal(ErrorCode.JobMeshModelUnavailable, result.ErrorCode);
    }

    /// <summary>
    /// **3D 의 입력이 네 방향 이미지다.** 이미지 없이 받아 두면 앞 세 단계를 다 치르고
    /// 나서 아무 일도 일어나지 않는다.
    /// </summary>
    [Fact]
    public async Task MeshWithoutImages_IsRejected()
    {
        var (fixture, upload, text, _) = await ReadyAsync();
        var mesh = await SeedMeshProviderAsync(fixture);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            imageProviderConfigId: null, imageModel: null,
            meshProviderConfigId: mesh, meshModel: StubModelCatalog.DefaultMeshModel);

        Assert.Equal(ErrorCode.JobMeshRequiresImages, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownMeshModel_IsRejected()
    {
        var (fixture, upload, text, image) = await ReadyAsync();
        var mesh = await SeedMeshProviderAsync(fixture);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            image, StubModelCatalog.DefaultImageModel,
            mesh, "P9-does-not-exist");

        // 목록에 없는 모델은 워커가 집는 순간 실패한다 — 접수 자리에서 막는다
        Assert.Equal(ErrorCode.JobMeshModelUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task DisabledMeshProvider_IsRejected()
    {
        var (fixture, upload, text, image) = await ReadyAsync();
        var mesh = await SeedMeshProviderAsync(fixture, enabled: false);

        var result = await fixture.Start.HandleAsync(
            AssetCategory.Background, upload, text, StubModelCatalog.DefaultModel, default,
            image, StubModelCatalog.DefaultImageModel,
            mesh, StubModelCatalog.DefaultMeshModel);

        Assert.Equal(ErrorCode.JobMeshProviderDisabled, result.ErrorCode);
    }

    // ─── 설정 ───

    private static async Task<(PipelineFixture Fixture, Guid Upload, Guid Text, Guid Image)> ReadyAsync()
    {
        var fixture = new PipelineFixture();

        return (
            fixture,
            await fixture.SeedUploadAsync(),
            await fixture.SeedProviderAsync(),
            await fixture.SeedProviderAsync());
    }

    private static async Task<Guid> SeedMeshProviderAsync(PipelineFixture fixture, bool enabled = true)
    {
        var config = ProviderConfig.Create(
            "Tripo 운영", ProviderKind.Tripo,
            apiKeyCipher: "cipher", apiKeyLast4: "9a1b", fixture.Clock.Now);

        if (!enabled)
        {
            config.Update("Tripo 운영", ProviderKind.Tripo, null, null, isEnabled: false, fixture.Clock.Now);
        }

        await fixture.Providers.AddAsync(config, CancellationToken.None);
        return config.Id;
    }
}
