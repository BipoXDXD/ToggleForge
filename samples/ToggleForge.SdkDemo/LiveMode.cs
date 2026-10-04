using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// O SDK contra uma API de verdade: mostra a flag mudando enquanto o estúdio publica e reverte pelo console. A URL
/// e a credencial vêm de variáveis de ambiente, nunca da linha de comando, que fica no histórico do shell.
/// </summary>
internal static class LiveMode
{
    private const string UrlVariable = "TOGGLEFORGE_API_URL";
    private const string CredentialVariable = "TOGGLEFORGE_CREDENTIAL";

    private static readonly TimeSpan _displayEvery = TimeSpan.FromSeconds(5);

    /// <param name="read">Avalia a flag para o jogador e descreve o resultado numa linha.</param>
    /// <returns>O código de saída do processo.</returns>
    public static async Task<int> RunAsync(string flagKey, string playerId, Func<ToggleForgeClient, string, EvaluationContext, string> read)
    {
        if (!Uri.TryCreate(Environment.GetEnvironmentVariable(UrlVariable), UriKind.Absolute, out var apiUrl))
        {
            Console.Error.WriteLine($"Defina {UrlVariable} com a raiz da API, por exemplo https://toggleforge.example.com/.");
            return ExitCodes.InvalidInput;
        }

        if (!KeepsCredentialPrivate(apiUrl))
        {
            Console.Error.WriteLine($"{UrlVariable} precisa de https; http só vale para localhost, porque a credencial iria em texto claro.");
            return ExitCodes.InvalidInput;
        }

        if (!SdkCredential.TryParse(Environment.GetEnvironmentVariable(CredentialVariable), out var credential))
        {
            Console.Error.WriteLine($"Defina {CredentialVariable} com a credencial do ambiente (tfsdk_...), copiada do console.");
            return ExitCodes.InvalidInput;
        }

        if (!PlayerId.TryParse(playerId, out var player))
        {
            Console.Error.WriteLine($"O jogador \"{playerId}\" não é um identificador válido.");
            return ExitCodes.InvalidInput;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, pressed) =>
        {
            pressed.Cancel = true;
            stop.Cancel();
        };

        using var http = new HttpClient { BaseAddress = apiUrl };
        var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToggleForge", "sdk-demo");
        await using var game = new ToggleForgeClient(
            new HttpSnapshotTransport(http, credential),
            new FileSnapshotStore(cacheDirectory, credential),
            new ToggleForgeOptions { SyncInterval = ToggleForgeOptions.MinSyncInterval });
        Console.WriteLine($"ToggleForge ao vivo: {flagKey} para {playerId} em {apiUrl}. Ctrl+C encerra.");
        Console.WriteLine($"Cache em {cacheDirectory}");
        try
        {
            await game.StartAsync(stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }

        await ShowChangesAsync(() => read(game, flagKey, EvaluationContext.ForPlayer(player)), stop.Token);
        return ExitCodes.Success;
    }

    private static bool KeepsCredentialPrivate(Uri apiUrl) =>
        apiUrl.Scheme == Uri.UriSchemeHttps || (apiUrl.Scheme == Uri.UriSchemeHttp && apiUrl.IsLoopback);

    /// <summary>Uma linha a cada mudança de valor, motivo, origem ou versão, até o Ctrl+C.</summary>
    private static async Task ShowChangesAsync(Func<string> describe, CancellationToken stop)
    {
        string? last = null;
        while (!stop.IsCancellationRequested)
        {
            var line = describe();
            if (line != last)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss}  {line}");
                last = line;
            }

            try
            {
                await Task.Delay(_displayEvery, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
