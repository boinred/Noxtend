using Noxtend.Domain.Job;
using Noxtend.Domain.Sprites;

namespace Noxtend.Tests.Domain;

public sealed class SpriteRulesTests
{
    private static readonly SpriteSettings Layers = new(SpriteView.SideView, SpriteOutputKind.Layers);
    private static readonly SpriteCanvas Source = new(800, 600);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OutputCanvas_Layers_PreservesRatioWithoutUpscale()
    {
        Assert.Equal(new SpriteCanvas(1024, 512), SpriteRules.OutputCanvas(Layers, new(2048, 1024)));
        Assert.Equal(new SpriteCanvas(640, 320), SpriteRules.OutputCanvas(Layers, new(640, 320)));
        Assert.Equal(new SpriteCanvas(512, 1024), SpriteRules.OutputCanvas(Layers, new(1024, 2048)));
        Assert.Equal(new SpriteCanvas(1, 1024), SpriteRules.OutputCanvas(Layers, new(1, 4096)));
        Assert.Equal(new SpriteCanvas(1024, 513), SpriteRules.OutputCanvas(Layers, new(2048, 1025)));
    }

    [Fact]
    public void OutputCanvas_IsometricTile_UsesDiamondCell()
        => Assert.Equal(new SpriteCanvas(128, 64),
            SpriteRules.OutputCanvas(new(SpriteView.Isometric, SpriteOutputKind.Tiles), Source));

    [Theory]
    [InlineData(SpriteView.SideView, 64)]
    [InlineData(SpriteView.TopDown, 128)]
    [InlineData(SpriteView.TopDown, 256)]
    public void OutputCanvas_SquareTile_UsesSelectedWidth(SpriteView view, int width)
        => Assert.Equal(new SpriteCanvas(width, width),
            SpriteRules.OutputCanvas(new(view, SpriteOutputKind.Tiles, width), Source));

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(4096, 4096, true)]
    [InlineData(4096, 4097, false)]
    [InlineData(1, 16777216, true)]
    [InlineData(1, 16777217, false)]
    [InlineData(0, 600, false)]
    [InlineData(800, 0, false)]
    [InlineData(-1, 600, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    public void ValidateSettings_ChecksSourcePixelBoundary(int width, int height, bool expected)
        => Assert.Equal(expected, SpriteRules.ValidateSettings(Layers, new(width, height)).IsSuccess);

    [Theory]
    [InlineData(63, false)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    [InlineData(127, false)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    [InlineData(255, false)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void ValidateSettings_ChecksTileWidth(int width, bool expected)
        => Assert.Equal(expected, SpriteRules.ValidateSettings(Layers with { TileWidth = width }, Source).IsSuccess);

    [Fact]
    public void ValidateSettings_RejectsNullAndUndefinedEnums()
    {
        Assert.False(SpriteRules.ValidateSettings(null!, Source).IsSuccess);
        Assert.False(SpriteRules.ValidateSettings(Layers, null!).IsSuccess);
        Assert.False(SpriteRules.ValidateSettings(Layers with { View = (SpriteView)99 }, Source).IsSuccess);
        Assert.False(SpriteRules.ValidateSettings(Layers with { OutputKind = (SpriteOutputKind)99 }, Source).IsSuccess);
        Assert.False(SpriteRules.ValidateSettings(Layers with { Repeat = (SpriteRepeat)99 }, Source).IsSuccess);
        foreach (var repeat in Enum.GetValues<SpriteRepeat>())
        {
            Assert.True(SpriteRules.ValidateSettings(Layers with { Repeat = repeat }, Source).IsSuccess);
        }
    }

    [Theory]
    [InlineData(0, 0, 1, 1, true)]
    [InlineData(0.5, 0.5, 0.5, 0.5, true)]
    [InlineData(0.5, 0, 0.50001, 1, false)]
    [InlineData(0, 0.5, 1, 0.50001, false)]
    [InlineData(-0.00001, 0, 1, 1, false)]
    [InlineData(0, -0.00001, 1, 1, false)]
    [InlineData(0, 0, 0, 1, false)]
    [InlineData(0, 0, 1, 0, false)]
    [InlineData(0, 0, -0.00001, 1, false)]
    [InlineData(double.NaN, 0, 1, 1, false)]
    [InlineData(0, double.PositiveInfinity, 1, 1, false)]
    [InlineData(0, 0, double.PositiveInfinity, 1, false)]
    [InlineData(0, 0, 1, double.NegativeInfinity, false)]
    public void Validate_ChecksStrictFiniteBounds(double x, double y, double w, double h, bool expected)
        => Assert.Equal(expected, SpriteRules.Validate(Layers, Source,
            [Plan() with { SourceBounds = new(x, y, w, h) }]).IsSuccess);

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Validate_ChecksLoopFrames(int frames, bool expected)
        => Assert.Equal(expected, SpriteRules.Validate(Layers, Source,
            [Plan() with { Loop = true, FrameCount = frames }]).IsSuccess);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Validate_ChecksFps(int fps, bool expected)
        => Assert.Equal(expected, SpriteRules.Validate(Layers, Source, [Plan() with { Fps = fps }]).IsSuccess);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Validate_ChecksTargetCount(int count, bool expected)
        => Assert.Equal(expected, SpriteRules.Validate(Layers, Source,
            Enumerable.Range(0, count).Select(i => Plan(i)).ToArray()).IsSuccess);

    [Theory]
    [InlineData(499, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void Validate_ChecksMotionNotes(int length, bool expected)
        => Assert.Equal(expected, SpriteRules.Validate(Layers, Source,
            [Plan() with { MotionNotes = new string('가', length) }]).IsSuccess);

    [Theory]
    [InlineData(6, 3, 3, true)]
    [InlineData(8, 0, 0, true)]
    [InlineData(8, 0, 1, false)]
    public void Validate_ChecksTotalFramesIncludingBases(int eightFrameLoops, int fourFrameLoops, int statics, bool expected)
    {
        var plans = Enumerable.Range(0, eightFrameLoops + fourFrameLoops + statics)
            .Select(i => Plan(i) with
            {
                Loop = i < eightFrameLoops + fourFrameLoops,
                FrameCount = i < eightFrameLoops ? 8 : 4,
            }).ToArray();
        Assert.Equal(expected, SpriteRules.Validate(Layers, Source, plans).IsSuccess);
    }

    [Fact]
    public void Validate_RejectsInvalidRequiredPlanData()
    {
        Assert.False(SpriteRules.Validate(Layers, Source, null!).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [null!]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [Plan() with { Name = " " }]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [Plan() with { Name = null! }]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [Plan() with { SourceBounds = null! }]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [Plan() with { MotionNotes = null! }]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [Plan(), Plan()]).IsSuccess);
        var first = Plan();
        Assert.False(SpriteRules.Validate(Layers, Source, [first, Plan(1) with { Id = first.Id }]).IsSuccess);
        Assert.True(SpriteRules.Validate(Layers, Source, [first, Plan(1) with { Name = first.Name }]).IsSuccess);
    }

    [Fact]
    public void Validate_EnforcesTransparencyBackToFront()
    {
        var back = Plan(5) with { RequiresTransparency = false };
        var front = Plan(10);
        Assert.True(SpriteRules.Validate(Layers, Source, [front, back]).IsSuccess);
        Assert.False(SpriteRules.Validate(Layers, Source, [front with { RequiresTransparency = false }, back]).IsSuccess);
        var square = new SpriteSettings(SpriteView.TopDown, SpriteOutputKind.Tiles);
        Assert.True(SpriteRules.Validate(square, Source, [back, front with { RequiresTransparency = false }]).IsSuccess);
        Assert.False(SpriteRules.Validate(square with { View = SpriteView.Isometric }, Source, [back]).IsSuccess);
    }

    [Fact]
    public void Transform_ContainsGenerationCanvasInOutput()
    {
        Assert.Equal(new SpriteTransform(0.5, 0, 128), SpriteRules.Transform(new(1024, 512), new(512, 512)));
        Assert.Equal(new SpriteTransform(0.5, 128, 0), SpriteRules.Transform(new(512, 1024), new(512, 512)));
        Assert.Equal(new SpriteTransform(2, 0, 0), SpriteRules.Transform(new(64, 32), new(128, 64)));
    }

    [Fact]
    public void LegacyCreate_WithoutMesh_RemainsThreeD()
    {
        var job = PipelineJob.Create(AssetCategory.Background, Guid.NewGuid(), Now);
        Assert.Equal(ProductionMode.ThreeD, job.ProductionMode);
        Assert.Null(job.Sprites);
    }

    [Fact]
    public void CreateSprites_StoresInitialStateWithoutPlanningTasks()
    {
        var sourceId = Guid.NewGuid();
        var imageProviderId = Guid.NewGuid();
        var result = PipelineJob.CreateSprites(sourceId, imageProviderId,
            "image-model", Layers, Source, new(1024, 1024), Now);
        Assert.True(result.IsSuccess);
        var job = result.Value!;
        Assert.Equal(sourceId, job.SourceImageId);
        Assert.Equal(imageProviderId, job.ImageProviderConfigId);
        Assert.Equal("image-model", job.ImageModel);
        Assert.Equal(AssetCategory.Background, job.Category);
        Assert.Equal(ProductionMode.TwoD, job.ProductionMode);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(Now, job.CreatedAt);
        Assert.True(job.RequiresReview);
        Assert.Empty(job.Tasks);
        var state = job.Sprites!;
        Assert.Equal(Layers, state.Settings);
        Assert.Equal(Source, state.SourceCanvas);
        Assert.Equal(new SpriteCanvas(1024, 1024), state.GenerationCanvas);
        Assert.Equal(Source, state.OutputCanvas);
        Assert.Equal(new SpriteTransform(600.0 / 1024, 100, 0), state.Transform);
        Assert.Equal(SpritePhase.Analyzing, state.Phase);
        Assert.Equal(0, state.ReviewRevision);
    }

    [Theory]
    [InlineData(0, 1024)]
    [InlineData(1024, -1)]
    public void CreateSprites_RejectsInvalidGenerationCanvas(int width, int height)
        => Assert.False(PipelineJob.CreateSprites(Guid.NewGuid(), Guid.NewGuid(),
            "image", Layers, Source, new(width, height), Now).IsSuccess);

    [Fact]
    public void RoleOf_UsesDefaultsAndNormalizesOnlyDefaultRole()
    {
        var tiles = Layers with { OutputKind = SpriteOutputKind.Tiles };
        Assert.Equal(SpriteAssetRole.Layer, SpriteRules.RoleOf(Layers, Plan()));
        Assert.Equal(SpriteAssetRole.Tile, SpriteRules.RoleOf(tiles, Plan()));
        Assert.Equal(SpriteAssetRole.Object, SpriteRules.RoleOf(tiles, Plan() with { Role = SpriteAssetRole.Object }));
        Assert.Null(SpriteRules.NormalizeRole(tiles, Plan() with { Role = SpriteAssetRole.Tile }).Role);
        Assert.Null(SpriteRules.NormalizeRole(Layers, Plan() with { Role = SpriteAssetRole.Layer }).Role);
        Assert.Equal(SpriteAssetRole.Object, SpriteRules.NormalizeRole(tiles,
            Plan() with { Role = SpriteAssetRole.Object }).Role);
    }

    [Theory]
    [InlineData(SpriteOutputKind.Layers, SpriteAssetRole.Tile)]
    [InlineData(SpriteOutputKind.Layers, SpriteAssetRole.Object)]
    [InlineData(SpriteOutputKind.Layers, SpriteAssetRole.Backdrop)]
    [InlineData(SpriteOutputKind.Tiles, SpriteAssetRole.Layer)]
    [InlineData(SpriteOutputKind.Tiles, (SpriteAssetRole)99)]
    public void Validate_RejectsRolesOutsideOutputKind(SpriteOutputKind kind, SpriteAssetRole role)
    {
        var result = SpriteRules.Validate(Layers with { OutputKind = kind }, Source,
            [Plan() with { Role = role }], kind == SpriteOutputKind.Layers ? null : new(1));
        Assert.False(result.IsSuccess);
        Assert.Equal("결과 유형에 허용되지 않는 에셋 역할입니다", result.ErrorMessage);
    }

    [Fact]
    public void Validate_EnforcesBackdropOrderAndRoleTransparency()
    {
        var tiles = Layers with { OutputKind = SpriteOutputKind.Tiles };
        var backdrop = Plan() with { Role = SpriteAssetRole.Backdrop, RequiresTransparency = false };
        var obj = Plan(1) with { Role = SpriteAssetRole.Object };
        Assert.True(SpriteRules.Validate(tiles, Source, [backdrop, obj], new(1)).IsSuccess);
        Assert.False(SpriteRules.Validate(tiles, Source, [backdrop, backdrop with { Id = Guid.NewGuid(), Order = 1 }], new(1)).IsSuccess);
        Assert.False(SpriteRules.Validate(tiles, Source, [backdrop with { Order = 2 }, obj], new(1)).IsSuccess);
        Assert.False(SpriteRules.Validate(tiles, Source, [obj with { RequiresTransparency = false }], new(1)).IsSuccess);
        Assert.False(SpriteRules.Validate(tiles, Source, [backdrop with { RequiresTransparency = true }], new(1)).IsSuccess);
        Assert.Equal("격자 칸 너비가 필요합니다", SpriteRules.Validate(tiles, Source, [obj]).ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Validate_RejectsInvalidGrid(double width)
        => Assert.False(SpriteRules.Validate(Layers with { OutputKind = SpriteOutputKind.Tiles }, Source,
            [Plan()], new(width)).IsSuccess);

    [Fact]
    public void Validate_LayersRejectsGrid()
        => Assert.False(SpriteRules.Validate(Layers, Source, [Plan()], new(0.5)).IsSuccess);

    [Fact]
    public void CellWidth_UsesExplicitGridOrTileMedian()
    {
        var tiles = Layers with { OutputKind = SpriteOutputKind.Tiles };
        var plans = new[] { Plan() with { SourceBounds = new(0, 0, 0.2, 1) },
            Plan(1) with { SourceBounds = new(0, 0, 0.4, 1) },
            Plan(2) with { Role = SpriteAssetRole.Object } };
        Assert.Equal(0.6, SpriteRules.CellWidth(new(0.6), plans, tiles));
        Assert.Equal(0.3, SpriteRules.CellWidth(null, plans, tiles)!.Value, 10);
        Assert.Equal(0.4, SpriteRules.CellWidth(null, [.. plans, Plan(3) with { SourceBounds = new(0, 0, 0.8, 1) }], tiles));
        Assert.Null(SpriteRules.CellWidth(null, [plans[2]], tiles));
        Assert.Null(SpriteRules.CellWidth(null, [Plan()], Layers));
    }

    [Fact]
    public void AssetOutput_UsesWorldScaleAndCapsBackdrop()
    {
        var tiles = Layers with { OutputKind = SpriteOutputKind.Tiles };
        var source = new SpriteCanvas(1536, 1024);
        var obj = Plan() with { Role = SpriteAssetRole.Object, SourceBounds = new(0.1, 0.1, 0.2, 0.25) };
        var output = SpriteRules.AssetOutput(tiles, source, 128.0 / 1536, obj);
        Assert.True(output.IsSuccess);
        Assert.Equal((new SpriteCanvas(307, 256), 1.0), output.Value);
        var backdrop = SpriteRules.AssetOutput(tiles, source, 128.0 / 1536, obj with { Role = SpriteAssetRole.Backdrop });
        Assert.Equal((new SpriteCanvas(1024, 683), 1024.0 / 1536), backdrop.Value);
        var tooLarge = obj with { SourceBounds = new(0, 0, 1025.0 / 1536, 0.25) };
        Assert.Contains("오브젝트", SpriteRules.AssetOutput(tiles, source, 128.0 / 1536, tooLarge).ErrorMessage!);
        Assert.False(SpriteRules.Validate(tiles, source, [tooLarge], new(128.0 / 1536)).IsSuccess);
        Assert.False(SpriteRules.AssetOutput(tiles, source, 128.0 / 1536,
            obj with { SourceBounds = new(0, 0, 0.00001, 0.25) }).IsSuccess);
    }

    [Theory]
    [InlineData(0.5, 1, true)]
    [InlineData(1024.49, 1024, true)]
    [InlineData(1024.5, 0, false)]
    public void AssetOutput_RoundsAwayFromZeroBeforeCheckingBoundary(double width, int expected, bool success)
    {
        var result = SpriteRules.AssetOutput(Layers with { OutputKind = SpriteOutputKind.Tiles },
            new(1536, 1024), 128.0 / 1536,
            Plan() with { Role = SpriteAssetRole.Object, SourceBounds = new(0, 0, width / 1536, 0.25) });
        Assert.Equal(success, result.IsSuccess);
        if (success)
            Assert.Equal(expected, result.Value.Canvas.Width);
    }

    [Theory]
    [InlineData(SpriteAssetRole.Layer, 0, 0)]
    [InlineData(SpriteAssetRole.Backdrop, 0, 0)]
    [InlineData(SpriteAssetRole.Tile, 153.5, 128)]
    [InlineData(SpriteAssetRole.Object, 153.5, 128)]
    public void Anchor_UsesRoleAndFractionalCenter(SpriteAssetRole role, double x, double y)
        => Assert.Equal(new SpriteAnchor(x, y), SpriteRules.Anchor(role, new(307, 256)));

    [Fact]
    public void AssetCanvas_ReusesJobCanvasOrSelectsClosestAspectRatio()
    {
        var tiles = Layers with { OutputKind = SpriteOutputKind.Tiles };
        var source = new SpriteCanvas(1536, 1024);
        var generation = new SpriteCanvas(1024, 1024);
        var tile = SpriteRules.AssetCanvas(tiles, source, generation, null, Plan(), []);
        Assert.Equal(new SpriteAssetCanvas(new(128, 128), generation,
            SpriteRules.Transform(generation, new(128, 128)), 1), tile.Value);
        var layer = SpriteRules.AssetCanvas(Layers, source, generation, null, Plan(), []);
        Assert.Equal(SpriteRules.OutputCanvas(Layers, source), layer.Value!.Output);
        var obj = SpriteRules.AssetCanvas(tiles, source, generation, 128.0 / 1536,
            Plan() with { Role = SpriteAssetRole.Object, SourceBounds = new(0.1, 0.1, 0.2, 0.25) },
            [new(512, 1024), new(1024, 512), new(1024, 1024)]);
        Assert.Equal(new SpriteCanvas(1024, 1024), obj.Value!.Generation);
        Assert.Equal(SpriteRules.Transform(obj.Value.Generation, new(307, 256)), obj.Value.Transform);
    }

    private static SpriteAssetPlan Plan(int order = 0)
        => new(Guid.NewGuid(), $"대상 {order}", order, new(0, 0, 1, 1), true);
}
