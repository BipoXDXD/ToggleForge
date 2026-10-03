using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Http;

namespace ToggleForge.Api;

/// <summary>
/// Limite global em memória, por instância (ADR 0006). Roda depois da autorização, quando já se sabe quem
/// é o cliente: membro pela conta, jogo pela credencial e IP, o resto pelo IP.
/// </summary>
internal static class ApiRateLimiting
{
    private const string UnknownAddress = "unknown";
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddToggleForgeRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimits>().BindConfiguration(RateLimits.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimits>>((options, limits) =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => PartitionOf(context, limits.Value));
            // O Problem Details do OnRejected já leva o 429; RejectionStatusCode não chega a valer.
            options.OnRejected = RejectAsync;
        });
        return services;
    }

    private static RateLimitPartition<string> PartitionOf(HttpContext context, RateLimits limits)
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? UnknownAddress;
        if (context.User.FindFirstValue(SdkCredentialHandler.CredentialClaim) is { } credentialId)
        {
            return PerMinute($"game:{credentialId}:{address}", limits.GameClientPerMinute);
        }

        if (context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } memberId)
        {
            return PerMinute($"member:{memberId}", limits.MemberPerMinute);
        }

        return PerMinute($"anonymous:{address}", limits.AnonymousPerMinute);
    }

    private static RateLimitPartition<string> PerMinute(string key, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = _window,
            QueueLimit = 0,
        });

    private static async ValueTask RejectAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : _window;
        rejected.HttpContext.Response.Headers.RetryAfter = RetryAfter.Seconds(wait);
        await Problems.TooManyRequests("Too many requests. Wait and try again.").ExecuteAsync(rejected.HttpContext);
    }
}
