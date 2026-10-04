using System.Diagnostics.CodeAnalysis;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>O formulário de flag nova no rascunho.</summary>
public sealed class NewFlag
{
    public string Key { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public decimal RolloutPercent { get; set; }

    /// <returns>Falso se o percentual não vira pontos-base (fora de 0–100 ou com mais de duas casas).</returns>
    public bool TryGetCreation([NotNullWhen(true)] out FlagCreation? creation)
    {
        if (!RolloutShare.TryToBasisPoints(RolloutPercent, out var basisPoints))
        {
            creation = null;
            return false;
        }

        // A booleana nasce como o console sempre a criou: incluído recebe true, excluído recebe false.
        creation = new FlagCreation(Key, FlagTypes.Boolean, Enabled, FlagValue.True, FlagValue.False, basisPoints);
        return true;
    }
}
