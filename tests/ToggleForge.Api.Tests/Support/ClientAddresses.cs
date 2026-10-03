using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ToggleForge.Api.Tests.Support;

/// <summary>O TestServer não tem IP de cliente; nos testes de rate limit, o IP vem deste header.</summary>
public sealed class ClientAddresses : IStartupFilter
{
    public const string Header = "X-Test-Client-Address";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (context.Request.Headers[Header] is [{ } address])
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
            }

            return nextMiddleware(context);
        });
        next(app);
    };
}
