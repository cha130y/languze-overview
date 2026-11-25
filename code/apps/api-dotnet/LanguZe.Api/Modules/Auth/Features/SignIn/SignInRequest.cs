using LanguZe.Api.Platform.Validation;

namespace LanguZe.Api.Modules.Auth.Features.SignIn;

/// <summary>NestJS's SignInDto, decorator for decorator; it documents no limits.</summary>
public sealed record SignInRequest(
    [property: IsEmail, MaxLength(254)] string Email,
    [property: IsString, MaxLength(128)] string Password);
