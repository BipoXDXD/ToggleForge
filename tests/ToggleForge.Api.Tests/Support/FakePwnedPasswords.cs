using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// O Pwned Passwords dos testes, por ser dependência externa não gerenciada: responde pela lista daqui, com
/// linhas de padding como o serviço real, e guarda tudo o que a API mandou. Nenhum teste sai para a internet.
/// </summary>
public sealed class FakePwnedPasswords(HttpStatusCode status = HttpStatusCode.OK, TimeSpan delay = default)
{
    /// <summary>Aparece em vazamentos.</summary>
    public const string BreachedPassword = "breached password 2026";

    /// <summary>Volta na resposta só como linha de padding, com contagem 0.</summary>
    public const string PaddingOnlyPassword = "padding only password 2026";

    private readonly ConcurrentQueue<SentRequest> _requests = new();
    private readonly HttpStatusCode _status = status;
    private readonly TimeSpan _delay = delay;
    private int _abandonedRequests;

    public IReadOnlyCollection<SentRequest> Requests => _requests;

    /// <summary>Requisições que a API abandonou antes da resposta, por timeout.</summary>
    public int AbandonedRequests => _abandonedRequests;

#pragma warning disable CA5350 // SHA-1 é o identificador do protocolo do Pwned Passwords.
    public static string Sha1Hex(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private static string RangeOf(string prefix)
    {
        var lines = new List<string> { "0000000000000000000000000000000000A:3" };
        foreach (var (password, count) in new[] { (BreachedPassword, 42), (PaddingOnlyPassword, 0) })
        {
            var hash = Sha1Hex(password);
            if (hash.StartsWith(prefix, StringComparison.Ordinal))
            {
                lines.Add($"{hash[5..]}:{count}");
            }
        }

        return string.Join("\r\n", lines);
    }

    /// <summary>O que saiu da API: o endereço completo e todos os headers, como texto.</summary>
    public sealed record SentRequest(Uri Uri, string Headers);

    private sealed class Handler(FakePwnedPasswords service) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            service._requests.Enqueue(new SentRequest(request.RequestUri!, request.Headers.ToString()));
            if (service._delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(service._delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref service._abandonedRequests);
                    throw;
                }
            }

            if (service._status != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(service._status);
            }

            var prefix = request.RequestUri!.Segments[^1];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(RangeOf(prefix)) };
        }
    }
}
