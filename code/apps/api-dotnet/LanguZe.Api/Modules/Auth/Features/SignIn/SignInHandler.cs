using LanguZe.Api.Data;
using LanguZe.Api.Data.Entities;
using LanguZe.Api.Modules.Auth.Domain;
using LanguZe.Api.Modules.Auth.Shared;
using LanguZe.Api.Platform.Records;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LanguZe.Api.Modules.Auth.Features.SignIn;

/// <summary>
/// Signs in with email (US-003). An unknown address and a wrong password are refused alike,
/// and an unknown address still costs a hash, as Better Auth does, so neither the answer
/// nor its time tells whether an account exists. A suspended account is refused after the
/// password check, and every session it has is ended.
/// </summary>
internal sealed class SignInHandler(UserManager<User> users, LanguZeDbContext db, AccountReader accounts, TrafficLog traffic, AddressLimits limits)
{
    public async Task<MeResponse> HandleAsync(SignInRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!BetterAuthRules.IsEmail(request.Email))
        {
            throw AuthErrors.CouldNotComplete();
        }

        // Before the password is checked: many machines must not try many passwords on one account.
        limits.CountSignIn(context, request.Email);

        var user = await users.FindByEmailAsync(request.Email.ToLowerInvariant());
        if (user?.PasswordHash is null)
        {
            users.PasswordHasher.HashPassword(new User(), request.Password);
            // The account when the address has one, never the address typed (FR-110).
            await traffic.RecordAsync(TrafficEventKind.SignInFailed, user?.Id, "password", cancellationToken);
            throw AuthErrors.InvalidCredentials();
        }

        // A Better Auth hash that matches is rewritten in Identity's format here.
        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await traffic.RecordAsync(TrafficEventKind.SignInFailed, user.Id, "password", cancellationToken);
            throw AuthErrors.InvalidCredentials();
        }

        if (user.Status == AccountStatus.Suspended)
        {
            // The password was right, so the person may be told.
            await db.Sessions.Where(session => session.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await traffic.RecordAsync(TrafficEventKind.SignInFailed, user.Id, "password", cancellationToken);
            throw AuthErrors.AccountSuspended();
        }

        await traffic.RecordAsync(TrafficEventKind.SignIn, user.Id, "password", cancellationToken);
        await SessionSignIn.SignInAsync(context, user);
        return await accounts.ReadAsync(user.Id, cancellationToken);
    }
}
