namespace ToggleForge.Evaluation.Tests;

internal static class NewShopFlag
{
    public const string ReferencePlayer = "player-1";

    /// <summary>Bucket do <see cref="ReferencePlayer"/> nesta flag, conforme Vectors/rollout-v1.json.</summary>
    public const int ReferencePlayerBucket = 7_370;

    public static ProjectKey Project { get; } = ProjectKey.Parse("studio-game");

    public static EnvironmentKey Environment { get; } = EnvironmentKey.Parse("prod");

    public static FlagKey Key { get; } = FlagKey.Parse("new-shop");

    public static FlagIdentity Identity { get; } = new(Project, Environment, Key);

    public static RolloutSeed Seed { get; } = RolloutSeed.Parse("seed-1");
}
