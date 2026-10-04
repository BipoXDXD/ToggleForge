using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

internal static class FlagResultText
{
    /// <returns>Por exemplo <c>new-shop = true (Included, Server, v2)</c>.</returns>
    public static string Describe(string flagKey, FlagResult<bool> result) =>
        Describe(flagKey, result, result.Value ? "true" : "false");

    /// <param name="value">O valor do resultado como o jogo o mostraria.</param>
    public static string Describe<T>(string flagKey, FlagResult<T> result, string value) =>
        $"{flagKey} = {value} ({result.Reason}, {result.Source}{(result.SnapshotVersion is { } version ? $", v{version}" : string.Empty)})";
}
