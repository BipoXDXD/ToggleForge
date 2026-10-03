using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class DraftComparisonTests
{
    [Fact]
    public void Identical_content_has_no_changes()
    {
        PublishedFlag[] flags = [Flags.Published("new-shop")];

        Assert.Empty(DraftComparison.Changes(flags, [Flags.Published("new-shop")]));
    }

    [Fact]
    public void Flags_only_in_the_draft_are_added_and_flags_only_published_are_removed()
    {
        var changes = DraftComparison.Changes([Flags.Published("new-shop")], [Flags.Published("old-shop")]);

        Assert.Equal(
            [new DraftChange(Flags.Published("new-shop").Key, DraftChangeKind.Added),
             new DraftChange(Flags.Published("old-shop").Key, DraftChangeKind.Removed)],
            changes);
    }

    [Theory]
    [InlineData(2_000, "seed-1")]
    [InlineData(1_000, "seed-2")]
    public void Different_settings_or_seed_under_the_same_key_is_a_modification(int basisPoints, string seed)
    {
        var changes = DraftComparison.Changes(
            [Flags.Published("new-shop", basisPoints, seed)], [Flags.Published("new-shop", 1_000, "seed-1")]);

        Assert.Equal(DraftChangeKind.Modified, Assert.Single(changes).Kind);
    }

    [Fact]
    public void Changes_are_ordered_by_key()
    {
        var changes = DraftComparison.Changes(
            [Flags.Published("zeta"), Flags.Published("alpha")], [Flags.Published("mid")]);

        Assert.Equal(["alpha", "mid", "zeta"], changes.Select(change => change.FlagKey.Value));
    }
}
