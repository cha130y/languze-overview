using LanguZe.Api.Modules.Worlds.Shared;

namespace LanguZe.Api.Modules.Worlds.Features.ListWorlds;

internal sealed class ListWorldsHandler(WorldReader reader)
{
    public Task<List<WorldSummaryResponse>> HandleAsync(Guid learnerId, CancellationToken cancellationToken) =>
        reader.ListSummariesAsync(learnerId, cancellationToken);
}
