using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Domain.Authoring;

/// <summary>Resumo opcional que o Editor escreve ao publicar.</summary>
public sealed record ChangeSummary
{
    public const int MaxLength = 280;

    private ChangeSummary(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out ChangeSummary? summary)
    {
        summary = FreeText.IsValid(value, MaxLength) && value.Length > 0 ? new ChangeSummary(value) : null;
        return summary is not null;
    }

    public override string ToString() => Value;
}
