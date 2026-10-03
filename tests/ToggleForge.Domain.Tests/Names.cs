namespace ToggleForge.Domain.Tests;

internal static class Names
{
    public static DisplayName Of(string value) =>
        DisplayName.TryParse(value, out var name) ? name : throw new ArgumentException($"Invalid name '{value}'.", nameof(value));
}
