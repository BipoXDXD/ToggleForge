using ToggleForge.SdkDemo;

// Sem argumentos, o roteiro autocontido (é o smoke test do CI). Com --live, a API de verdade; --json lê uma flag JSON.
const string Usage = "Uso: dotnet run --project samples/ToggleForge.SdkDemo [-- --live [--json] [flag] [jogador]]";
const int MaxLiveArguments = 2;
const string DefaultLiveFlag = "new-shop";
const string DefaultLiveJsonFlag = "shop-config";
const string DefaultLivePlayer = "player-42";

switch (args)
{
    case []:
        return await ScriptedTour.RunAsync();
    case ["--live", "--json", .. var live] when live.Length <= MaxLiveArguments:
        return await LiveMode.RunAsync(live.ElementAtOrDefault(0) ?? DefaultLiveJsonFlag, live.ElementAtOrDefault(1) ?? DefaultLivePlayer, LiveReadings.Json);
    case ["--live", .. var live] when live.Length <= MaxLiveArguments:
        return await LiveMode.RunAsync(live.ElementAtOrDefault(0) ?? DefaultLiveFlag, live.ElementAtOrDefault(1) ?? DefaultLivePlayer, LiveReadings.Boolean);
    default:
        Console.Error.WriteLine(Usage);
        return ExitCodes.InvalidInput;
}
