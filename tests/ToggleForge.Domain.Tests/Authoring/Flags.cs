using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

internal static class Flags
{
    public static FlagSettings Settings(int basisPoints = 1_000, bool isEnabled = true) =>
        new(isEnabled, OnValue: true, OffValue: false, RolloutPercentage.FromBasisPoints(basisPoints));

    public static PublishedFlag Published(string key, int basisPoints = 1_000, string seed = "seed-1") =>
        new(FlagKey.Parse(key), Settings(basisPoints), RolloutSeed.Parse(seed));

    public static ProjectEnvironment NewEnvironment()
    {
        var created = Project.Create(Guid.NewGuid(), ProjectKey.Parse("studio-game"), Names.Of("Studio Game"), existingProjects: 0);
        Assert.True(created.TryGetValue(out var newProject, out _));
        return newProject.Environments[0];
    }

    public static void Add(ProjectEnvironment environment, string key, int basisPoints = 1_000)
    {
        var added = environment.AddFlag(FlagKey.Parse(key), Settings(basisPoints), RolloutSeed.Parse($"seed-{key}"));
        Assert.True(added.TryGetValue(out _, out var failure), failure?.Detail);
    }
}
