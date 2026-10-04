using ToggleForge.SdkDemo;

// Sem argumentos, o roteiro autocontido (é o smoke test do CI). Com --live, a API de verdade.
const string Usage = "Uso: dotnet run --project samples/ToggleForge.SdkDemo [-- --live [flag] [jogador]]";
const int MaxLiveArguments = 2;
const string DefaultLiveFlag = "new-shop";
const string DefaultLivePlayer = "player-42";

switch (args)
{
    case []:
        return await ScriptedTour.RunAsync();
    case ["--live", .. var live] when live.Length <= MaxLiveArguments:
        return await LiveMode.RunAsync(live.ElementAtOrDefault(0) ?? DefaultLiveFlag, live.ElementAtOrDefault(1) ?? DefaultLivePlayer, LiveReadings.Boolean);
    default:
        Console.Error.WriteLine(Usage);
        return ExitCodes.InvalidInput;
}
