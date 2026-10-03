using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Domain;

/// <summary>Nome exibido no console. Não é identificador: pode mudar sem afetar o rollout.</summary>
public sealed record DisplayName
{
    public const int MaxLength = 100;

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out DisplayName? name)
    {
        name = FreeText.IsValid(value, MaxLength) && !string.IsNullOrWhiteSpace(value) ? new DisplayName(value) : null;
        return name is not null;
    }

    public override string ToString() => Value;
}
