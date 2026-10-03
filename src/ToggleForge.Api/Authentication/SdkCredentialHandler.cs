using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ToggleForge.Api.Http;
using ToggleForge.Application.Delivery;

namespace ToggleForge.Api.Authentication;

/// <summary>Autentica o jogo pela credencial do ambiente; o ambiente vem da credencial, nunca do cliente.</summary>
internal sealed class SdkCredentialHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SnapshotService snapshots)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SdkCredential";
    public const string EnvironmentClaim = "toggleforge:environment";

    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var credential = await snapshots.AuthenticateAsync(authorization[BearerPrefix.Length..], Context.RequestAborted);
        if (credential is null)
        {
            return AuthenticateResult.Fail("Invalid credential.");
        }

        var identity = new ClaimsIdentity([new Claim(EnvironmentClaim, credential.EnvironmentId.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        await Problems.Unauthorized("Send a valid environment credential as a Bearer token.").ExecuteAsync(Context);
    }
}
