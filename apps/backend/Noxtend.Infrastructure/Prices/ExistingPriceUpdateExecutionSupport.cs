using Noxtend.Domain.Provider;
using Noxtend.Infrastructure.Image;
using Noxtend.Infrastructure.Mesh;
using Noxtend.Tuning.Application.Prices;

namespace Noxtend.Infrastructure.Prices;

internal sealed class ExistingPriceUpdateExecutionSupport : IPriceUpdateExecutionSupport
{
    public IReadOnlyCollection<(string Provider, string Model, string Area)> SupportedModels { get; } =
        Enum.GetValues<ProviderKind>().SelectMany(kind =>
            ImageModels.For(kind).Select(model => (kind.ToString().ToLowerInvariant(), model.Id, "image"))
                .Concat(MeshModels.For(kind).Select(model => (kind.ToString().ToLowerInvariant(), model.Id, "mesh"))))
            .ToArray();
}
