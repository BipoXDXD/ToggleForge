using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>O formulário de edição de uma flag: começa com os valores lidos e diz o que mudou neles.</summary>
public sealed class FlagEdit
{
    public bool Enabled { get; set; }

    public decimal RolloutPercent { get; set; }

    public bool OnValue { get; set; }

    public bool OffValue { get; set; }

    public static FlagEdit Of(Flag flag) => new()
    {
        Enabled = flag.Enabled,
        RolloutPercent = RolloutShare.ToPercent(flag.RolloutBasisPoints),
        OnValue = flag.OnValue,
        OffValue = flag.OffValue,
    };

    /// <param name="original">A flag como foi lida da API.</param>
    /// <param name="changes">Só os campos diferentes de <paramref name="original"/>; vazio se nada mudou.</param>
    /// <returns>Falso se o percentual não vira pontos-base (fora de 0–100 ou com mais de duas casas).</returns>
    public bool TryGetChanges(Flag original, out FlagModification changes)
    {
        if (!RolloutShare.TryToBasisPoints(RolloutPercent, out var basisPoints))
        {
            changes = new FlagModification();
            return false;
        }

        changes = new FlagModification(
            Enabled: Changed(original.Enabled, Enabled),
            OnValue: Changed(original.OnValue, OnValue),
            OffValue: Changed(original.OffValue, OffValue),
            RolloutBasisPoints: Changed(original.RolloutBasisPoints, basisPoints));
        return true;
    }

    private static T? Changed<T>(T original, T edited)
        where T : struct =>
        EqualityComparer<T>.Default.Equals(original, edited) ? null : edited;
}
