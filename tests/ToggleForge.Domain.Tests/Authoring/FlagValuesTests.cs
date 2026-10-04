using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class FlagValuesTests
{
    [Fact]
    public void A_boolean_flag_reads_true_and_false()
    {
        Assert.True(FlagValues.Of(FlagType.Boolean, JsonFlagValue.True, JsonFlagValue.False).TryGetValue(out var values, out _));
        Assert.Equal(new BooleanValues(On: true, Off: false), values);
    }

    [Theory]
    [InlineData("1", "false", true)]
    [InlineData("\"true\"", "false", true)]
    [InlineData("true", "null", false)]
    [InlineData("true", "{}", false)]
    public void A_boolean_flag_refuses_anything_else_on_the_side_that_has_it(string on, string off, bool onIsWrong)
    {
        Assert.False(FlagValues.Of(FlagType.Boolean, JsonFlagValue.Parse(on), JsonFlagValue.Parse(off)).TryGetValue(out _, out var failure));
        Assert.Equal(onIsWrong ? AuthoringFailures.OnValueNotOfFlagType : AuthoringFailures.OffValueNotOfFlagType, failure);
    }

    [Fact]
    public void A_json_flag_takes_any_json_value_including_true_and_null()
    {
        Assert.True(FlagValues.Of(FlagType.Json, JsonFlagValue.True, JsonFlagValue.Parse("null")).TryGetValue(out var values, out _));
        var json = Assert.IsType<JsonValues>(values);
        Assert.Equal(("true", "null"), (json.OnValue.Text, json.OffValue.Text));
        Assert.Equal(FlagType.Json, json.Type);
    }

    [Fact]
    public void Json_values_count_the_bytes_of_both_sides()
    {
        var values = new JsonValues(JsonFlagValue.Parse("{\"discount\":0.2}"), JsonFlagValue.Parse("{}"));

        Assert.Equal(18, values.ByteCount);
    }

    [Fact]
    public void Boolean_values_are_stored_as_the_json_literals()
    {
        var values = new BooleanValues(On: false, Off: true);

        Assert.Equal((JsonFlagValue.False, JsonFlagValue.True), (values.OnValue, values.OffValue));
    }

    [Fact]
    public void Restore_refuses_a_stored_boolean_flag_with_a_json_value() =>
        Assert.Throws<InvalidDataException>(() => FlagValues.Restore(FlagType.Boolean, JsonFlagValue.Parse("3"), JsonFlagValue.False));
}
