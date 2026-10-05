using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class NewFlagTests
{
    [Fact]
    public void A_new_flag_starts_as_a_boolean()
    {
        var form = new NewFlag();

        Assert.Equal(FlagTypes.Boolean, form.Type);
        Assert.False(form.IsJson);
    }

    [Fact]
    public void A_boolean_flag_is_created_with_true_and_false_whatever_the_value_texts_say()
    {
        var form = new NewFlag { Key = "new-shop", RolloutPercent = 12.5m, OnValueText = "not json" };

        Assert.True(form.TryGetCreation(out var creation, out var errors));
        Assert.Equal(new FlagCreation("new-shop", FlagTypes.Boolean, true, FlagValue.True, FlagValue.False, 1_250), creation);
        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void A_json_flag_takes_the_values_typed_compacted()
    {
        var form = new NewFlag
        {
            Key = "shop-config",
            Type = FlagTypes.Json,
            RolloutPercent = 100,
            OnValueText = "{\n  \"discount\": 0.2\n}",
            OffValueText = "{\"discount\": 0}",
        };

        Assert.True(form.TryGetCreation(out var creation, out var errors));
        Assert.Equal(
            new FlagCreation("shop-config", FlagTypes.Json, true, Values.Json("""{"discount":0.2}"""), Values.Json("""{"discount":0}"""), 10_000),
            creation);
        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void A_json_flag_can_start_with_null()
    {
        var form = new NewFlag { Key = "shop-config", Type = FlagTypes.Json, OnValueText = "1", OffValueText = "null" };

        Assert.True(form.TryGetCreation(out var creation, out _));
        Assert.Equal(FlagValue.Null, creation.OffValue);
    }

    [Fact]
    public void Each_json_value_that_is_missing_is_refused_on_its_field()
    {
        var form = new NewFlag { Key = "shop-config", Type = FlagTypes.Json };

        Assert.False(form.TryGetCreation(out var creation, out var errors));
        Assert.Null(creation);
        Assert.Equal("Enter a JSON value for included players.", errors.For(FieldErrors.OnValue));
        Assert.Equal("Enter a JSON value for everyone else.", errors.For(FieldErrors.OffValue));
    }

    [Fact]
    public void A_json_value_that_is_not_json_is_refused_on_its_field()
    {
        var form = new NewFlag { Key = "shop-config", Type = FlagTypes.Json, OnValueText = "{discount: 0.2}", OffValueText = "{}" };

        Assert.False(form.TryGetCreation(out _, out var errors));
        Assert.Equal("The value for included players is not valid JSON.", errors.For(FieldErrors.OnValue));
        Assert.Null(errors.For(FieldErrors.OffValue));
    }

    [Fact]
    public void A_json_value_that_repeats_a_key_is_refused_on_its_field()
    {
        var form = new NewFlag { Key = "shop-config", Type = FlagTypes.Json, OnValueText = """{"a":1,"a":2}""", OffValueText = "{}" };

        Assert.False(form.TryGetCreation(out _, out var errors));
        Assert.Equal("The value for included players repeats a key in an object. Each key can appear only once.", errors.For(FieldErrors.OnValue));
    }

    [Fact]
    public void A_rollout_that_is_not_basis_points_is_refused_on_its_field()
    {
        var form = new NewFlag { Key = "new-shop", RolloutPercent = 12.345m };

        Assert.False(form.TryGetCreation(out var creation, out var errors));
        Assert.Null(creation);
        Assert.Equal("Enter a rollout between 0 and 100, with at most two decimals.", errors.For(FieldErrors.Rollout));
    }

    [Fact]
    public void A_bad_rollout_and_bad_values_are_all_reported_at_once()
    {
        var form = new NewFlag { Key = "shop-config", Type = FlagTypes.Json, RolloutPercent = 101 };

        Assert.False(form.TryGetCreation(out _, out var errors));
        Assert.NotNull(errors.For(FieldErrors.Rollout));
        Assert.NotNull(errors.For(FieldErrors.OnValue));
        Assert.NotNull(errors.For(FieldErrors.OffValue));
    }
}
