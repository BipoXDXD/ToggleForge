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

    /// <returns>O código de saída do processo.</returns>
    public static async Task<int> RunAsync(string flagKey, string playerId)
    {
        if (!Uri.TryCreate(Environment.GetEnvironmentVariable(UrlVariable), UriKind.Absolute, out var apiUrl)
            || !SdkCredential.TryParse(Environment.GetEnvironmentVariable(CredentialVariable), out var credential)
            || !PlayerId.TryParse(playerId, out var player))
        {
            Console.Error.WriteLine($"Defina {UrlVariable} (a raiz da API) e {CredentialVariable} (a credencial do ambiente, tfsdk_...).");
            return 2;
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
        await game.StartAsync(stop.Token);

        string? last = null;
        while (!stop.IsCancellationRequested)
        {
            var result = game.EvaluateBoolean(flagKey, EvaluationContext.ForPlayer(player), defaultValue: false);
            var line = $"{flagKey} = {(result.Value ? "true" : "false")} ({result.Reason}, {result.Source}{(result.SnapshotVersion is { } version ? $", v{version}" : string.Empty)})";
            if (line != last)
            {
                Console.WriteLine($"{DateTimeOffset.Now:HH:mm:ss}  {line}");
                last = line;
            }

            try
            {
                await Task.Delay(_displayEvery, stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return 0;
    }
}
