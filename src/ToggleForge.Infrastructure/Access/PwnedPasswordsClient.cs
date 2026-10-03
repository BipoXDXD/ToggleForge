using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ToggleForge.Infrastructure.Access;

/// <summary>
/// Adapter da API Pwned Passwords por k-anonymity (ADR 0007): só os 5 primeiros hex do SHA-1 da senha saem
/// da API, com padding para o tamanho da resposta não revelar o prefixo. Senha, hash completo e e-mail nunca
/// saem; a comparação do resto do hash é local.
/// </summary>
internal sealed partial class PwnedPasswordsClient(HttpClient http, ILogger<PwnedPasswordsClient> logger)
{
    public static readonly Uri BaseAddress = new("https://api.pwnedpasswords.com/");

    private const int PrefixLength = 5;
    private const char CountSeparator = ':';

    public async Task<PasswordExposure> CheckAsync(string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(password);

        var hash = Sha1Hex(password);
        var prefix = hash[..PrefixLength];
        var suffix = hash[PrefixLength..];
        string range;
        try
        {
            range = await DownloadRangeAsync(prefix, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            LogUnavailable(logger, exception.GetType().Name);
            return PasswordExposure.Unknown;
        }

        return IsListed(range, suffix) ? PasswordExposure.Exposed : PasswordExposure.NotFound;
    }

    /// <summary>
    /// O SHA-1 aqui não protege nada: é o identificador que o protocolo do Pwned Passwords usa. A senha guardada
    /// segue com o hashing do Identity.
    /// </summary>
#pragma warning disable CA5350 // SHA-1 é imposto pelo protocolo do Pwned Passwords, não usado como proteção.
    private static string Sha1Hex(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350

    private async Task<string> DownloadRangeAsync(string prefix, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
        request.Headers.Add("Add-Padding", "true");
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Cada linha é "SUFIXO:CONTAGEM"; as de padding vêm com contagem 0 e não contam.</summary>
    private static bool IsListed(string range, string suffix) =>
        range.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(CountSeparator))
            .Any(fields => fields is [var listed, var count]
                && listed.Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var times)
                && times > 0);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pwned Passwords unavailable ({Failure}); accepting the password unchecked")]
    private static partial void LogUnavailable(ILogger logger, string failure);
}
