namespace Noxtend.Domain.Sprites;

public sealed record SpriteSourceCandidate(Guid Id, string BlobKey, string ContentType, int Width, int Height,
    string Prompt, DateTimeOffset CreatedAt);

// 원본 확정 후에도 보존하는 프롬프트 모드 출신 기록
public sealed record SpriteSourceDraft(string Description, string? Prompt, Guid TextProviderConfigId, string TextModel,
    IReadOnlyList<SpriteSourceCandidate> Candidates)
{
    public const int MaxCandidates = 8;
    public const int MaxDescription = 1000;
    public const int MaxPrompt = 4000;

    public static bool ValidDescription(string? value)
        => value?.Trim() is { Length: > 0 and <= MaxDescription };

    public static bool ValidPrompt(string? value)
        => value?.Trim() is { Length: > 0 and <= MaxPrompt };
}

public sealed record SpriteSourceInput(string? Description, string? Prompt);
