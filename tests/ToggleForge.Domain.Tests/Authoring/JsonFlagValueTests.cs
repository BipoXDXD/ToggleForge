using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class JsonFlagValueTests
{
    [Theory]
    [InlineData("3", "3")]
    [InlineData("\"gold\"", "\"gold\"")]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("[ 1 , 2 ]", "[1,2]")]
    [InlineData("{ \"b\" : 1 ,\n \"a\" : [ 1, { } ] }", "{\"b\":1,\"a\":[1,{}]}")]
    [InlineData("1.50", "1.50")]
    [InlineData("\"ação\"", "\"ação\"")]
    public void Any_json_value_is_kept_compact_with_its_keys_in_order(string json, string expected)
    {
        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal(expected, value.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("tru")]
    [InlineData("{'a':1}")]
    [InlineData("1 2")]
    [InlineData("{\"a\":1,}")]
    [InlineData("\"\\uD800\"")]
    [InlineData("{\"\\uDC00\":1}")]
    [InlineData("[\"\\uD83D\"]")]
    public void Text_that_is_not_one_json_value_is_refused(string? json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out var value, out var problem));
        Assert.Null(value);
        Assert.Equal(JsonValueProblem.NotJson, problem);
    }

    [Fact]
    public void Text_with_a_lone_surrogate_is_refused()
    {
        var json = "\"" + '\uD800' + "\"";

        Assert.False(JsonFlagValue.TryParse(json, out var value, out var problem));
        Assert.Null(value);
        Assert.Equal(JsonValueProblem.NotJson, problem);
    }

    [Fact]
    public void Parse_rejects_a_lone_surrogate() =>
        Assert.Throws<ArgumentException>(() => JsonFlagValue.Parse("\"\\uD800\""));

    [Theory]
    [InlineData("\"\\u00e7\"", "\"ç\"")]
    [InlineData("\"ç\"", "\"ç\"")]
    [InlineData("\"a\\/b\"", "\"a/b\"")]
    public void Escapes_are_written_in_one_canonical_form(string json, string expected) =>
        Assert.Equal(expected, JsonFlagValue.Parse(json).Text);

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}")]
    [InlineData("{\"x\":{\"a\":1,\"a\":1}}")]
    [InlineData("[{\"a\":1,\"\\u0061\":2}]")]
    public void A_key_repeated_in_an_object_is_refused_at_any_depth(string json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.DuplicateKey, problem);
    }

    [Fact]
    public void The_same_key_in_different_objects_is_fine() =>
        Assert.True(JsonFlagValue.TryParse("[{\"a\":1},{\"a\":2}]", out _, out _));

    [Fact]
    public void Keys_that_differ_only_in_case_are_different_keys()
    {
        Assert.True(JsonFlagValue.TryParse("{\"a\":1,\"A\":2}", out var value, out _));
        Assert.Equal("{\"a\":1,\"A\":2}", value.Text);
    }

    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    public void Values_nest_up_to_8_levels(int levels, bool accepted)
    {
        var json = new string('[', levels) + "1" + new string(']', levels);

        Assert.Equal(accepted, JsonFlagValue.TryParse(json, out _, out var problem));
        if (!accepted)
        {
            Assert.Equal(JsonValueProblem.TooDeep, problem);
        }
    }

    [Fact]
    public void Objects_count_as_levels_too()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 9)) + "1" + new string('}', 9);

        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.TooDeep, problem);
    }

    [Fact]
    public void Objects_nest_up_to_8_levels()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 8)) + "1" + new string('}', 8);

        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal(json, value.Text);
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("[1, 1e309]")]
    public void A_number_out_of_double_range_is_rejected(string json)
    {
        Assert.False(JsonFlagValue.TryParse(json, out _, out var problem));
        Assert.Equal(JsonValueProblem.NumberOutOfRange, problem);
    }

    [Theory]
    [InlineData("1e308")]
    [InlineData("1e-400")]
    [InlineData("12345678901234567890")]
    public void A_number_a_double_can_hold_is_kept_as_written(string json)
    {
        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal(json, value.Text);
    }

    [Theory]
    [InlineData('x', 4094, true)]
    [InlineData('x', 4095, false)]
    [InlineData('ç', 2047, true)]
    [InlineData('ç', 2048, false)]
    public void Up_to_4_KiB_in_utf8_after_compaction(char filler, int count, bool accepted)
    {
        var json = "\"" + new string(filler, count) + "\"";

        Assert.Equal(accepted, JsonFlagValue.TryParse(json, out var value, out var problem));
        if (accepted)
        {
            Assert.Equal(4096, value!.ByteCount);
        }
        else
        {
            Assert.Equal(JsonValueProblem.TooLarge, problem);
        }
    }

    [Fact]
    public void Whitespace_does_not_count_toward_the_limit()
    {
        var json = "[" + new string(' ', 5_000) + "1]";

        Assert.True(JsonFlagValue.TryParse(json, out var value, out _));
        Assert.Equal("[1]", value.Text);
    }

    [Fact]
    public void The_same_json_written_differently_is_the_same_value() =>
        Assert.Equal(JsonFlagValue.Parse("{\"a\": [1, 2]}"), JsonFlagValue.Parse("{ \"a\":[1,2] }"));

    [Fact]
    public void True_and_false_are_the_json_literals()
    {
        Assert.Equal(JsonFlagValue.Parse("true"), JsonFlagValue.True);
        Assert.Equal(JsonFlagValue.Parse("false"), JsonFlagValue.False);
    }

    [Fact]
    public void Parse_rejects_invalid_text() =>
        Assert.Throws<ArgumentException>(() => JsonFlagValue.Parse("{"));
}
