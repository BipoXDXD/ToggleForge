using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

internal static class FlagResultText
{
    /// <returns>Por exemplo <c>new-shop = true (Included, Server, v2)</c>.</returns>
    public static string Describe(string flagKey, FlagResult<bool> result) =>
        $"{flagKey} = {(result.Value ? "true" : "false")} ({result.Reason}, {result.Source}{(result.SnapshotVersion is { } version ? $", v{version}" : string.Empty)})";
}
