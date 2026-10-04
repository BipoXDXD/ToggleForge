using ToggleForge.SdkDemo;

// Sem argumentos, o roteiro autocontido (é o smoke test do CI). Com --live, a API de verdade:
// dotnet run --project samples/ToggleForge.SdkDemo -- --live [flag] [jogador]
if (args is ["--live", .. var live])
{
    return await LiveMode.RunAsync(live.ElementAtOrDefault(0) ?? "new-shop", live.ElementAtOrDefault(1) ?? "player-42");
}

await ScriptedTour.RunAsync();
return 0;
