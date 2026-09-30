using System.Security.Claims;
using System.Text.Encodings.Web;
using AccessFlow.Directory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AccessFlow.Api.Identity;

/// <summary>
/// Identifies the caller by the trusted X-User-Id header without verifying authenticity (ADR 0001, BR-01, BR-02).
/// </summary>
public sealed class UserIdHeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDirectory directory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "UserIdHeader";
    public const string HeaderName = "X-User-Id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header))
            return AuthenticateResult.NoResult();

        if (!Guid.TryParse(header.ToString(), out var userId))
            return AuthenticateResult.Fail($"{HeaderName} is not a valid user id.");

        if (!await directory.UserExistsAsync(userId, Context.RequestAborted))
            return AuthenticateResult.Fail($"User {userId} does not exist.");

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Caller is not authenticated."));
}
