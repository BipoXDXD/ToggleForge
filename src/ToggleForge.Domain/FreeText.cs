using System.Diagnostics.CodeAnalysis;

namespace ToggleForge.Domain;

internal static class FreeText
{
    internal static bool IsValid([NotNullWhen(true)] string? value, int maxLength) =>
        value is not null && value.Length <= maxLength && !value.Any(char.IsControl);
}
