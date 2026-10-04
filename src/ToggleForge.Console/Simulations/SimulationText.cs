using System.Globalization;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Simulations;

/// <summary>Como o simulador explica ao membro o resultado de cada flag.</summary>
public static class SimulationText
{
    public static string Source(Simulation simulation) =>
        simulation.Version is { } version ? $"Version {version.ToString(CultureInfo.InvariantCulture)}" : "Draft";

    /// <summary>
    /// Um rollout inclui quem tem bucket abaixo dele; sem bucket, o rollout é de 0% ou de 100% e decide sem olhar
    /// o jogador. Motivo desconhecido (a lista pode crescer) aparece como a API o mandou.
    /// </summary>
    public static string Why(SimulationResult result) => (result.Reason, result.Bucket) switch
    {
        ("disabled", _) => "Disabled: everyone gets the off value.",
        ("included", { } bucket) => $"Bucket {Format(bucket)} is inside the rollout.",
        ("included", null) => "The rollout includes everyone.",
        ("excluded", { } bucket) => $"Bucket {Format(bucket)} is outside the rollout.",
        ("excluded", null) => "The rollout includes no one.",
        var (reason, _) => reason,
    };

    private static string Format(int bucket) => bucket.ToString(CultureInfo.InvariantCulture);
}
