using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>O formulário de edição de uma flag: começa com os valores lidos e diz o que mudou neles.</summary>
public sealed class FlagEdit
{
    public bool Enabled { get; set; }

    public decimal RolloutPercent { get; set; }

    /// <summary>O valor como texto JSON: <c>true</c> ou <c>false</c> numa flag booleana, indentado numa flag JSON.</summary>
    public string OnValueText { get; set; } = string.Empty;

    /// <inheritdoc cref="OnValueText"/>
    public string OffValueText { get; set; } = string.Empty;

    public static FlagEdit Of(Flag flag) => new()
    {
        Enabled = flag.Enabled,
        RolloutPercent = RolloutShare.ToPercent(flag.RolloutBasisPoints),
        OnValueText = flag.OnValue.Indented(),
        OffValueText = flag.OffValue.Indented(),
    };

    /// <param name="original">A flag como foi lida da API.</param>
    /// <param name="changes">
    /// Só os campos diferentes de <paramref name="original"/>, com os valores já compactados pelo console; vazio se
    /// nada mudou ou se há erro. Espaço em branco não conta como mudança.
    /// </param>
    /// <param name="errors">Por campo, o que impediu montar as mudanças; vazio quando o retorno é verdadeiro.</param>
    public bool TryGetChanges(Flag original, out FlagModification changes, out FieldErrors errors)
    {
        errors = new FieldErrors();
        var basisPoints = FlagFields.Rollout(RolloutPercent, errors);
        var onValue = EditedValue(OnValueText, original.OnValue, FieldErrors.OnValue, errors);
        var offValue = EditedValue(OffValueText, original.OffValue, FieldErrors.OffValue, errors);
        if (basisPoints is not { } points || onValue is null || offValue is null)
        {
            changes = new FlagModification();
            return false;
        }

        changes = new FlagModification(
            Enabled: Changed(original.Enabled, Enabled),
            OnValue: ChangedValue(original.OnValue, onValue),
            OffValue: ChangedValue(original.OffValue, offValue),
            RolloutBasisPoints: Changed(original.RolloutBasisPoints, points));
        return true;
    }

    // No merge patch, null quer dizer "apague o campo", e nenhum campo de flag pode ser apagado: o JSON null só entra
    // na criação (contrato, updateFlag). Um null que já estava lá e não mudou não vai no pedido, então passa.
    private static FlagValue? EditedValue(string text, FlagValue original, string field, FieldErrors errors)
    {
        var value = FlagFields.Value(text, field, errors);
        if (value is { IsNull: true } && value != original)
        {
            errors.Add(field, FlagFields.NullOnlyAtCreation);
            return null;
        }

        return value;
    }

    private static T? Changed<T>(T original, T edited)
        where T : struct =>
        EqualityComparer<T>.Default.Equals(original, edited) ? null : edited;

    private static FlagValue? ChangedValue(FlagValue original, FlagValue edited) => original == edited ? null : edited;
}
