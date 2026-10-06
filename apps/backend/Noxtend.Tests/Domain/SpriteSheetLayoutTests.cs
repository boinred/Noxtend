using Noxtend.Domain.Sprites;

namespace Noxtend.Tests.Domain;

public sealed class SpriteSheetLayoutTests
{
    [Fact]
    public void Frame1024_UsesThreeColumnsAndSecondPageAfterNineFrames()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
        var pages = SpriteRules.SheetPages(new(1024, 1024), ids);
        Assert.Equal(2, pages.Count);
        Assert.Equal(new SpriteRect(2, 2, 1024, 1024), pages[0].Cells[0].Rect);
        Assert.Equal(new SpriteRect(1030, 2, 1024, 1024), pages[0].Cells[1].Rect);
        Assert.Equal(new SpriteCanvas(3084, 3084), pages[0].Canvas);
        Assert.Equal(1, pages[1].Page);
        Assert.Equal(new SpriteCanvas(1028, 1028), pages[1].Canvas);
        Assert.Equal(Enumerable.Range(0, 10), pages.SelectMany(p => p.Cells).Select(c => c.FrameIndex));
        Assert.Equal(ids, pages.SelectMany(p => p.Cells).Select(c => c.ImageId));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, -1)]
    [InlineData(4093, 1)]
    [InlineData(1, 4093)]
    public void InvalidFrameSize_IsRejected(int width, int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => SpriteRules.SheetPages(new(width, height), [Guid.NewGuid()]));

    [Fact]
    public void RectangularFrame_UsesRowMajorPlacement()
    {
        var pages = SpriteRules.SheetPages(new(2044, 1020), Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray());
        Assert.Equal(new SpriteRect(2, 1026, 2044, 1020), pages[0].Cells[2].Rect);
        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => { Assert.InRange(p.Canvas.Width, 1, 4096); Assert.InRange(p.Canvas.Height, 1, 4096); });
    }
}
