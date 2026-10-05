using ToggleForge.Console.Api;

namespace ToggleForge.Console.Tests;

/// <summary>Valores de flag escritos à mão nos testes; texto que não é JSON é erro do teste, não do console.</summary>
internal static class Values
{
    public static FlagValue Json(string text) =>
        FlagValue.TryParse(text, out var value) ? value : throw new ArgumentException($"Not JSON: {text}", nameof(text));
}
