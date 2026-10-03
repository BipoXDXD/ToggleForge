using System.Net;
using System.Net.Sockets;
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
    private const int Ipv6NetworkPrefixLength = 64;
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
        var address = ClientNetwork(context.Connection.RemoteIpAddress);
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

    /// <summary>
    /// Um cliente IPv6 costuma ter um /64 inteiro e trocaria de endereço a cada requisição; o limite vale para
    /// a rede. IPv4 mapeado em IPv6 conta como o próprio IPv4.
    /// </summary>
    private static string ClientNetwork(IPAddress? address)
    {
        if (address is null)
        {
            return UnknownAddress;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        var networkBytes = Ipv6NetworkPrefixLength / 8;
        Array.Clear(bytes, networkBytes, bytes.Length - networkBytes);
        return $"{new IPAddress(bytes)}/{Ipv6NetworkPrefixLength}";
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
