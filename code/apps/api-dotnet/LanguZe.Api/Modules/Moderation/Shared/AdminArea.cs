using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Auth.Contracts;
using LanguZe.Api.Platform.Errors;
using LanguZe.Api.Platform.RateLimiting;

namespace LanguZe.Api.Modules.Moderation.Shared;

/// <summary>
/// What every route of the admin area has (admin.controller.ts, admin.guard.ts; FR-100, FR-102,
/// NFR-019, API design section 3.7): the ADMIN role, read from the database on every request,
/// and a limit of 30 a minute, since admins work by hand and a faster rate can only be a script.
/// </summary>
internal static class AdminArea
{
    /// <summary>
    /// The answer to anyone but an admin is 404, not 403: a 403 would confirm the route is there,
    /// which is enough to map the admin area by trying paths. It runs before the route's ID and
    /// query are read, as NestJS's guard runs before its pipes, so a learner learns nothing from
    /// a malformed request either. A request with no session was refused before this.
    /// </summary>
    public static RouteHandlerBuilder AdminOnly(this RouteHandlerBuilder route) =>
        route
            .AddEndpointFilter(async (context, next) =>
            {
                if (context.HttpContext.RequestServices.GetRequiredService<CurrentAccount>().Role != UserRole.Admin)
                {
                    throw new AppException(ErrorCodes.NotFound, StatusCodes.Status404NotFound, "Not found.");
                }

                return await next(context);
            })
            .WithRateLimit(30, TimeSpan.FromMinutes(1))
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status429TooManyRequests);
}
