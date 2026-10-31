using LanguZe.Api.Modules.Worlds.Shared;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Worlds.Features.ListWorlds;

internal sealed class ListWorldsHandler(WorldReader reader)
{
    public async Task<List<WorldSummaryResponse>> HandleAsync(Guid learnerId, CancellationToken cancellationToken)
    {
        var worlds = await reader.WithPhotosAndWords()
            .Where(world => world.LearnerId == learnerId)
            .OrderByDescending(world => world.CreatedAt)
            .ToListAsync(cancellationToken);

        var summaries = new List<WorldSummaryResponse>(worlds.Count);
        foreach (var world in worlds)
        {
            summaries.Add(await reader.SummaryOfAsync(world));
        }

        return summaries;
    }
}
