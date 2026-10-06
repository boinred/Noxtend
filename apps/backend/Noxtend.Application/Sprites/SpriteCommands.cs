using Noxtend.Domain.Sprites;

namespace Noxtend.Application.Sprites;

public sealed record StartSpriteJobCommand(Guid RequestId, Guid? UploadId, Guid? SourceJobId,
    Guid? SourceGeneratedImageId, Guid ProviderConfigId, string Model,
    Guid ImageProviderConfigId, string ImageModel, SpriteSettings Settings);
