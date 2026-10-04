using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class FlagEditingTests
{
    private static readonly FlagKey _newShop = FlagKey.Parse("new-shop");

    [Fact]
    public void Changing_a_flag_applies_only_the_given_fields_at_the_next_revision()
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(Rollout: RolloutPercentage.FromBasisPoints(5_000)));

        Assert.True(changed.TryGetValue(out var flag, out _));
        Assert.Equal(3, environment.DraftRevision);
        Assert.Equal(3, flag.ChangedAtRevision);
        Assert.Equal(Flags.Settings(basisPoints: 5_000), flag.Settings);
        Assert.Equal(RolloutSeed.Parse("seed-new-shop"), flag.Seed);
    }

    [Fact]
    public void Disabling_a_flag_changes_only_its_state_at_the_next_revision()
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(IsEnabled: false));

        Assert.True(changed.TryGetValue(out var flag, out _));
        Assert.Equal(3, environment.DraftRevision);
        Assert.Equal(3, flag.ChangedAtRevision);
        Assert.Equal(Flags.Settings(isEnabled: false), flag.Settings);
        Assert.Equal(RolloutSeed.Parse("seed-new-shop"), flag.Seed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Changing_with_another_revision_fails_and_keeps_the_flag(long expectedRevision)
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(_newShop, expectedRevision, new FlagChange(IsEnabled: false));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagChanged, failure);
        Assert.Equal(2, environment.DraftRevision);
        Assert.Equal(Flags.Settings(), Assert.Single(environment.Flags).Settings);
    }

    [Fact]
    public void Changing_a_flag_the_draft_does_not_have_fails()
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(FlagKey.Parse("battle-pass"), expectedRevision: 2, new FlagChange(IsEnabled: false));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagNotFound, failure);
    }

    [Fact]
    public void A_change_to_the_current_values_keeps_the_revision()
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(IsEnabled: true, OnValue: JsonFlagValue.True));

        Assert.True(changed.TryGetValue(out var flag, out _));
        Assert.Equal(2, environment.DraftRevision);
        Assert.Equal(2, flag.ChangedAtRevision);
    }

    [Fact]
    public void Changing_a_boolean_flag_to_a_value_that_is_not_true_or_false_is_refused_and_the_revision_stays()
    {
        var environment = EnvironmentWithNewShop();

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(OnValue: JsonFlagValue.Parse("\"yes\"")));

        Assert.False(changed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.OnValueNotOfFlagType, failure);
        Assert.Equal(2, environment.DraftRevision);
        Assert.Equal(Flags.Settings(), Assert.Single(environment.Flags).Settings);
    }

    [Fact]
    public void A_json_flag_takes_a_new_value_of_any_json_type()
    {
        var environment = Flags.NewEnvironment();
        var empty = JsonFlagValue.Parse("{}");
        environment.AddFlag(_newShop, new FlagSettings(IsEnabled: true, new JsonValues(empty, empty), RolloutPercentage.All), RolloutSeed.Parse("s1"), PublishedFlagTypes.None);

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(OnValue: JsonFlagValue.Parse("3")));

        Assert.True(changed.TryGetValue(out var flag, out _));
        var values = Assert.IsType<JsonValues>(flag.Settings.Values);
        Assert.Equal(("3", "{}"), (values.OnValue.Text, values.OffValue.Text));
        Assert.Equal(3, environment.DraftRevision);
    }

    [Fact]
    public void Editing_one_flag_keeps_the_revision_of_the_others()
    {
        var environment = EnvironmentWithNewShop();
        Flags.Add(environment, "battle-pass");

        var changed = environment.ChangeFlag(_newShop, expectedRevision: 2, new FlagChange(IsEnabled: false));

        Assert.True(changed.TryGetValue(out _, out _));
        Assert.Equal(3, environment.Flags.Single(flag => flag.Key.Value == "battle-pass").ChangedAtRevision);
    }

    [Fact]
    public void Removing_a_flag_takes_it_out_of_the_draft_at_the_next_revision()
    {
        var environment = EnvironmentWithNewShop();

        var removed = environment.RemoveFlag(_newShop, expectedRevision: 2);

        Assert.True(removed.TryGetValue(out var flag, out _));
        Assert.Equal(_newShop, flag.Key);
        Assert.Equal(3, environment.DraftRevision);
        Assert.Empty(environment.Flags);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Removing_with_another_revision_fails_and_keeps_the_flag(long expectedRevision)
    {
        var environment = EnvironmentWithNewShop();

        var removed = environment.RemoveFlag(_newShop, expectedRevision);

        Assert.False(removed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagChanged, failure);
        Assert.Equal(2, environment.DraftRevision);
        Assert.Single(environment.Flags);
    }

    [Fact]
    public void Removing_a_flag_the_draft_does_not_have_fails()
    {
        var environment = EnvironmentWithNewShop();

        var removed = environment.RemoveFlag(FlagKey.Parse("battle-pass"), expectedRevision: 2);

        Assert.False(removed.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.FlagNotFound, failure);
    }

    [Fact]
    public void A_removed_key_comes_back_with_the_new_seed()
    {
        var environment = EnvironmentWithNewShop();
        environment.RemoveFlag(_newShop, expectedRevision: 2);

        var added = environment.AddFlag(_newShop, Flags.Settings(), RolloutSeed.Parse("fresh-seed"), PublishedFlagTypes.None);

        Assert.True(added.TryGetValue(out var flag, out _));
        Assert.Equal(RolloutSeed.Parse("fresh-seed"), flag.Seed);
        Assert.Equal(4, flag.ChangedAtRevision);
    }

    private static ProjectEnvironment EnvironmentWithNewShop()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        return environment;
    }
}
