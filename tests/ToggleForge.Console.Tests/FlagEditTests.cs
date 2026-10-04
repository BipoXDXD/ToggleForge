using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class FlagEditTests
{
    private static readonly Flag _original = new("new-shop", FlagTypes.Boolean, Enabled: true, OnValue: FlagValue.True, OffValue: FlagValue.False, RolloutBasisPoints: 1_250);

    [Fact]
    public void The_form_starts_with_the_values_read()
    {
        var edit = FlagEdit.Of(_original);

        Assert.True(edit.Enabled);
        Assert.Equal(12.5m, edit.RolloutPercent);
        Assert.True(edit.OnValue);
        Assert.False(edit.OffValue);
    }

    [Fact]
    public void Untouched_values_are_not_a_change()
    {
        var edited = FlagEdit.Of(_original).TryGetChanges(_original, out var changes);

        Assert.True(edited);
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void Only_the_fields_that_changed_go_to_the_api()
    {
        var edit = FlagEdit.Of(_original);
        edit.RolloutPercent = 50;
        edit.OffValue = true;

        edit.TryGetChanges(_original, out var changes);

        Assert.Equal(new FlagModification(OffValue: FlagValue.True, RolloutBasisPoints: 5_000), changes);
    }

    [Theory]
    [InlineData(false, true, false, false, null, null)]
    [InlineData(true, false, false, null, false, null)]
    [InlineData(true, true, true, null, null, true)]
    public void Each_switch_is_a_change_of_its_own(
        bool enabled, bool onValue, bool offValue, bool? enabledSent, bool? onValueSent, bool? offValueSent)
    {
        var edit = FlagEdit.Of(_original);
        (edit.Enabled, edit.OnValue, edit.OffValue) = (enabled, onValue, offValue);

        edit.TryGetChanges(_original, out var changes);

        Assert.Equal(new FlagModification(Enabled: enabledSent, OnValue: ValueOf(onValueSent), OffValue: ValueOf(offValueSent)), changes);
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public void A_rollout_that_is_not_basis_points_is_refused(string percent)
    {
        var edit = FlagEdit.Of(_original);
        edit.RolloutPercent = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(edit.TryGetChanges(_original, out _));
    }

    private static FlagValue? ValueOf(bool? value) => value is { } sent ? FlagValue.Of(sent) : null;
}
