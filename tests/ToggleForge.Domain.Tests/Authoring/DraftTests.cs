using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class DraftTests
{
    [Fact]
    public void Adding_a_flag_records_it_at_the_next_draft_revision()
    {
        var environment = Flags.NewEnvironment();
        var settings = Flags.Settings(basisPoints: 2_500);

        var added = environment.AddFlag(FlagKey.Parse("new-shop"), settings, RolloutSeed.Parse("seed-1"), PublishedFlagTypes.None);

        Assert.True(added.TryGetValue(out var flag, out _));
        Assert.Equal(2, environment.DraftRevision);
        Assert.Equal(2, flag.ChangedAtRevision);
        Assert.Equal(settings, flag.Settings);
        Assert.Same(flag, Assert.Single(environment.Flags));
    }

    [Fact]
    public void Adding_an_existing_key_fails_and_keeps_the_draft()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");

        var added = environment.AddFlag(FlagKey.Parse("new-shop"), Flags.Settings(), RolloutSeed.Parse("other"), PublishedFlagTypes.None);

        Assert.False(added.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagKeyTaken, failure);
        Assert.Equal(2, environment.DraftRevision);
        Assert.Single(environment.Flags);
    }

    [Fact]
    public void Draft_accepts_up_to_200_flags()
    {
        var environment = Flags.NewEnvironment();
        for (var index = 0; index < 199; index++)
        {
            Flags.Add(environment, $"flag-{index}");
        }

        Flags.Add(environment, "flag-199");

        var added = environment.AddFlag(FlagKey.Parse("flag-200"), Flags.Settings(), RolloutSeed.Parse("seed"), PublishedFlagTypes.None);
        Assert.False(added.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagLimitReached, failure);
        Assert.Equal(200, environment.Flags.Count);
    }
}
