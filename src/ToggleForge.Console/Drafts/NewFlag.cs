using System.Diagnostics.CodeAnalysis;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>O formulário de flag nova no rascunho.</summary>
public sealed class NewFlag
{
    public string Key { get; set; } = string.Empty;

    /// <summary><see cref="FlagTypes.Boolean"/> ou <see cref="FlagTypes.Json"/>; escolhido aqui e nunca mais (ADR 0011).</summary>
    public string Type { get; set; } = FlagTypes.Boolean;

    public bool Enabled { get; set; } = true;

    public decimal RolloutPercent { get; set; }

    /// <summary>Só numa flag JSON: o texto como o membro o digitou. Fica guardado se ele voltar para booleana.</summary>
    public string OnValueText { get; set; } = string.Empty;

    /// <inheritdoc cref="OnValueText"/>
    public string OffValueText { get; set; } = string.Empty;

    public bool IsJson => FlagTypes.IsJson(Type);

    /// <param name="creation">O pedido, com os valores já compactados pelo console; nulo se há erro.</param>
    /// <param name="errors">Por campo, o que impediu montar o pedido; vazio quando o retorno é verdadeiro.</param>
    /// <remarks>O JSON <c>null</c> vale aqui: a criação é o único momento em que a API o aceita.</remarks>
    public bool TryGetCreation([NotNullWhen(true)] out FlagCreation? creation, out FieldErrors errors)
    {
        errors = new FieldErrors();
        var basisPoints = FlagFields.Rollout(RolloutPercent, errors);

        // A booleana nasce como o console sempre a criou: incluído recebe true, excluído recebe false.
        var (onValue, offValue) = IsJson
            ? (FlagFields.Value(OnValueText, FieldErrors.OnValue, errors), FlagFields.Value(OffValueText, FieldErrors.OffValue, errors))
            : (FlagValue.True, FlagValue.False);
        if (basisPoints is not { } points || onValue is null || offValue is null)
        {
            creation = null;
            return false;
        }

        creation = new FlagCreation(Key, Type, Enabled, onValue, offValue, points);
        return true;
    }
}
