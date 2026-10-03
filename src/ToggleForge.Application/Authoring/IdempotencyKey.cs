using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Application.Authoring;

/// <summary>Chave gerada pelo cliente: 16 a 64 caracteres em [A-Za-z0-9_-] (ADP cap. 26).</summary>
public sealed record IdempotencyKey
{
    public const int MinLength = 16;
    public const int MaxLength = 64;

    private IdempotencyKey(string value) => Value = value;

    public string Value { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out IdempotencyKey? key)
    {
        key = value is { Length: >= MinLength and <= MaxLength } && value.All(IsAllowed) ? new IdempotencyKey(value) : null;
        return key is not null;
    }

    public override string ToString() => Value;

    private static bool IsAllowed(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';
}
