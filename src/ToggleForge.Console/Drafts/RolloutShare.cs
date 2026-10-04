using System.Globalization;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// O membro escreve o percentual; a API recebe pontos-base (100 = 1%), inteiros de 0 a 10000, sem ponto
/// flutuante (ADR 0004).
/// </summary>
public static class RolloutShare
{
    private const int BasisPointsPerPercent = 100;
    private const decimal MaximumPercent = 100m;

    /// <returns>Falso se o percentual sai de 0–100 ou tem mais de duas casas decimais.</returns>
    public static bool TryToBasisPoints(decimal percent, out int basisPoints)
    {
        var scaled = percent * BasisPointsPerPercent;
        if (percent < 0 || percent > MaximumPercent || scaled != decimal.Truncate(scaled))
        {
            basisPoints = 0;
            return false;
        }

        basisPoints = (int)scaled;
        return true;
    }

    public static string Format(int basisPoints) =>
        ((decimal)basisPoints / BasisPointsPerPercent).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
