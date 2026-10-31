using LanguZe.Api.Data;
using LanguZe.Api.Modules.Worlds.Shared;

namespace LanguZe.Api.Modules.Worlds.Features.RenameWorld;

internal sealed class RenameWorldHandler(LanguZeDbContext db, WorldReader reader)
{
    public async Task<WorldDetailResponse> HandleAsync(Guid learnerId, Guid worldId, RenameWorldRequest request, CancellationToken cancellationToken)
    {
        var world = await reader.OwnAsync(learnerId, worldId, cancellationToken);
        world.Name = request.Name;
        await db.SaveChangesAsync(cancellationToken);
        return await reader.DetailOfAsync(world);
    }
}
