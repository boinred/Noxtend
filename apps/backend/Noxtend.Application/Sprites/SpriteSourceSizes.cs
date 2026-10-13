using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public static class SpriteSourceSizes
{
    // 가로형·정사각형 우선, 같으면 면적 최대
    public static SpriteCanvas Pick(IReadOnlyList<SpriteCanvas> sizes)
        => sizes.OrderByDescending(size => size.Width >= size.Height).ThenByDescending(size => (long)size.Width * size.Height).First();
}
