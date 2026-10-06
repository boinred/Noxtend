namespace Noxtend.Domain.Sprites;

public sealed class SpritePipelineState
{
    private SpritePipelineState()
    {
        // EF Core 재구성용
    }

    internal SpritePipelineState(SpriteSettings settings, SpriteCanvas sourceCanvas, SpriteCanvas generationCanvas)
    {
        Settings = settings;
        SourceCanvas = sourceCanvas;
        GenerationCanvas = generationCanvas;
        OutputCanvas = SpriteRules.OutputCanvas(settings, sourceCanvas);
        Transform = SpriteRules.Transform(generationCanvas, OutputCanvas);
        Phase = SpritePhase.Analyzing;
    }

    public SpriteSettings Settings { get; private set; } = null!;
    public SpriteCanvas SourceCanvas { get; private set; } = null!;
    public SpriteCanvas GenerationCanvas { get; private set; } = null!;
    public SpriteCanvas OutputCanvas { get; private set; } = null!;
    public SpriteTransform Transform { get; private set; } = null!;
    public SpritePhase Phase { get; private set; }
    public int ReviewRevision { get; private set; }
}
