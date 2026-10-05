using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class FlagEditTests
{
    private static readonly Flag _original = new(
        "new-shop", FlagTypes.Boolean, Enabled: true, OnValue: FlagValue.True, OffValue: FlagValue.False, RolloutBasisPoints: 1_250);

    private static readonly Flag _shopConfig = new(
        "shop-config", FlagTypes.Json, Enabled: true, OnValue: Values.Json("""{"discount":0.2}"""), OffValue: FlagValue.Null,
        RolloutBasisPoints: 10_000);

    [Fact]
    public void The_form_starts_with_the_values_read()
    {
        var edit = FlagEdit.Of(_original);

        Assert.True(edit.Enabled);
        Assert.Equal(12.5m, edit.RolloutPercent);
        Assert.Equal("true", edit.OnValueText);
        Assert.Equal("false", edit.OffValueText);
    }

    [Fact]
    public void Untouched_values_are_not_a_change()
    {
        var edited = FlagEdit.Of(_original).TryGetChanges(_original, out var changes, out var errors);

        Assert.True(edited);
        Assert.True(changes.IsEmpty);
        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Only_the_fields_that_changed_go_to_the_api()
    {
        var edit = FlagEdit.Of(_original);
        edit.RolloutPercent = 50;
        edit.OffValueText = "true";

        edit.TryGetChanges(_original, out var changes, out _);

        Assert.Equal(new FlagModification(OffValue: FlagValue.True, RolloutBasisPoints: 5_000), changes);
    }

    [Theory]
    [InlineData(false, "true", "false", false, null, null)]
    [InlineData(true, "false", "false", null, false, null)]
    [InlineData(true, "true", "true", null, null, true)]
    public void Each_switch_is_a_change_of_its_own(
        bool enabled, string onValue, string offValue, bool? enabledSent, bool? onValueSent, bool? offValueSent)
    {
        var edit = FlagEdit.Of(_original);
        (edit.Enabled, edit.OnValueText, edit.OffValueText) = (enabled, onValue, offValue);

        edit.TryGetChanges(_original, out var changes, out _);

        Assert.Equal(new FlagModification(Enabled: enabledSent, OnValue: ValueOf(onValueSent), OffValue: ValueOf(offValueSent)), changes);
    }

    [Theory]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public void A_rollout_that_is_not_basis_points_is_refused_on_its_field(string percent)
    {
        var edit = FlagEdit.Of(_original);
        edit.RolloutPercent = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(edit.TryGetChanges(_original, out var changes, out var errors));
        Assert.Equal("Enter a rollout between 0 and 100, with at most two decimals.", errors.For(FieldErrors.Rollout));
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void A_json_flag_starts_with_its_values_indented()
    {
        var edit = FlagEdit.Of(_shopConfig);

        Assert.Equal("{\n  \"discount\": 0.2\n}", edit.OnValueText);
        Assert.Equal("null", edit.OffValueText);
    }

    [Fact]
    public void Reformatting_a_json_value_is_not_a_change()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OnValueText = """{ "discount" : 0.2 }""";

        Assert.True(edit.TryGetChanges(_shopConfig, out var changes, out _));
        Assert.True(changes.IsEmpty);
    }

    [Fact]
    public void A_changed_json_value_goes_to_the_api_compacted()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OnValueText = "{\n  \"discount\": 0.5\n}";

        edit.TryGetChanges(_shopConfig, out var changes, out _);

        Assert.Equal(new FlagModification(OnValue: Values.Json("""{"discount":0.5}""")), changes);
        Assert.Equal("""{"discount":0.5}""", changes.OnValue?.Text);
    }

    [Fact]
    public void A_new_rollout_alone_leaves_the_json_values_out()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.RolloutPercent = 50;

        Assert.True(edit.TryGetChanges(_shopConfig, out var changes, out _));
        Assert.Equal(new FlagModification(RolloutBasisPoints: 5_000), changes);
    }

    [Theory]
    [InlineData("{\"discount\": }", "{}", FieldErrors.OnValue, "The value for included players is not valid JSON.")]
    [InlineData("{}", "nul", FieldErrors.OffValue, "The value for everyone else is not valid JSON.")]
    [InlineData("  ", "{}", FieldErrors.OnValue, "Enter a JSON value for included players.")]
    [InlineData("{}", "", FieldErrors.OffValue, "Enter a JSON value for everyone else.")]
    public void A_value_that_is_not_json_is_refused_on_its_field(string onValue, string offValue, string field, string message)
    {
        var edit = FlagEdit.Of(_shopConfig);
        (edit.OnValueText, edit.OffValueText) = (onValue, offValue);

        Assert.False(edit.TryGetChanges(_shopConfig, out var changes, out var errors));
        Assert.Equal(message, errors.For(field));
        Assert.True(changes.IsEmpty);
    }

    // A API recusa chave repetida com um erro genérico do corpo, sem dizer o campo; o console aponta o campo antes.
    [Theory]
    [InlineData("""{"discount":0.2,"discount":0.3}""")]
    [InlineData("""{"shop":{"open":true,"open":false}}""")]
    [InlineData("""[1,{"a":1,"b":2,"a":3}]""")]
    [InlineData("""{"a":1,"\u0061":2}""")]
    public void A_value_that_repeats_a_key_in_an_object_is_refused_on_its_field(string offValue)
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OffValueText = offValue;

        Assert.False(edit.TryGetChanges(_shopConfig, out var changes, out var errors));
        Assert.Equal("The value for everyone else repeats a key in an object. Each key can appear only once.", errors.For(FieldErrors.OffValue));
        Assert.True(changes.IsEmpty);
    }

    [Theory]
    [InlineData("""[{"a":1},{"a":2}]""")]
    [InlineData("""{"a":{"a":1}}""")]
    [InlineData("""{"a":1,"A":2}""")]
    public void The_same_key_in_different_objects_is_not_a_repetition(string onValue)
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OnValueText = onValue;

        Assert.True(edit.TryGetChanges(_shopConfig, out var changes, out _));
        Assert.Equal(Values.Json(onValue), changes.OnValue);
    }

    [Fact]
    public void Both_values_are_checked_at_once()
    {
        var edit = FlagEdit.Of(_shopConfig);
        (edit.OnValueText, edit.OffValueText) = ("[", "");

        edit.TryGetChanges(_shopConfig, out _, out var errors);

        Assert.NotNull(errors.For(FieldErrors.OnValue));
        Assert.NotNull(errors.For(FieldErrors.OffValue));
    }

    [Fact]
    public void A_value_too_deep_to_read_is_refused_with_the_level_limit()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OnValueText = new string('[', 513) + new string(']', 513);

        Assert.False(edit.TryGetChanges(_shopConfig, out _, out var errors));
        Assert.Equal("This value nests too deep. JSON values can have at most 8 levels.", errors.For(FieldErrors.OnValue));
    }

    [Fact]
    public void A_value_over_4_KiB_once_compacted_is_refused_on_its_field()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OffValueText = $"\"a{new string('é', 2_047)}\"";

        Assert.False(edit.TryGetChanges(_shopConfig, out _, out var errors));
        Assert.Equal(
            "The value for everyone else is over 4 KiB once compacted. Make it smaller: every player downloads it.",
            errors.For(FieldErrors.OffValue));
    }

    [Fact]
    public void A_value_of_exactly_4_KiB_once_compacted_goes_to_the_api()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OffValueText = $"  \"{new string('é', 2_047)}\"  ";

        Assert.True(edit.TryGetChanges(_shopConfig, out var changes, out _));
        Assert.Equal(4_096, System.Text.Encoding.UTF8.GetByteCount(changes.OffValue?.Text ?? string.Empty));
    }

    [Fact]
    public void A_json_flag_takes_null_only_when_it_is_created()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.OnValueText = " null ";

        Assert.False(edit.TryGetChanges(_shopConfig, out _, out var errors));
        Assert.Equal("null is accepted only when the flag is created. Use another value, such as {} or \"\".", errors.For(FieldErrors.OnValue));
    }

    [Fact]
    public void A_null_already_there_is_not_a_change()
    {
        var edit = FlagEdit.Of(_shopConfig);
        edit.RolloutPercent = 50;

        Assert.True(edit.TryGetChanges(_shopConfig, out var changes, out _));
        Assert.Null(changes.OffValue);
    }

    private static FlagValue? ValueOf(bool? value) => value switch
    {
        true => FlagValue.True,
        false => FlagValue.False,
        null => null,
    };
}
