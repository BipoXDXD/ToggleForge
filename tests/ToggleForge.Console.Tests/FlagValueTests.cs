using System.Text.Json;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Tests;

public sealed class FlagValueTests
{
    [Fact]
    public void A_value_from_the_api_keeps_its_keys_and_numbers_as_written()
    {
        var holder = JsonSerializer.Deserialize<ValueHolder>("""{"value":{ "b" : 1.50, "a" : [1e3, "ação"] }}""", JsonSerializerOptions.Web);

        Assert.Equal("""{"b":1.50,"a":[1e3,"ação"]}""", holder!.Value.Text);
    }

    [Fact]
    public void The_json_null_from_the_api_is_a_value_not_a_missing_field()
    {
        var holder = JsonSerializer.Deserialize<ValueHolder>("""{"value":null}""", JsonSerializerOptions.Web);

        Assert.Equal(FlagValue.Null, holder!.Value);
    }

    [Theory]
    [InlineData("{\"a\":[1,2]}")]
    [InlineData("null")]
    [InlineData("\"gold\"")]
    [InlineData("\"<b>é & ü</b>\"")]
    [InlineData("{\"tag\":\"<i>\"}")]
    public void A_value_goes_to_the_api_as_json_not_as_text(string json)
    {
        var body = JsonSerializer.Serialize(new ValueHolder(Values.Json(json)), JsonSerializerOptions.Web);

        Assert.Equal($$"""{"value":{{json}}}""", body);
    }

    [Theory]
    [InlineData("3", "3")]
    [InlineData(" \"gold\" ", "\"gold\"")]
    [InlineData("null", "null")]
    [InlineData("true", "true")]
    [InlineData("[ 1 ,\n 2 ]", "[1,2]")]
    [InlineData("{\n  \"b\": {},\n  \"a\": []\n}", "{\"b\":{},\"a\":[]}")]
    public void Text_with_one_json_value_is_kept_compact(string text, string expected)
    {
        Assert.True(FlagValue.TryParse(text, out var value));
        Assert.Equal(expected, value.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{discount: 0.2}")]
    [InlineData("{'a':1}")]
    [InlineData("1 2")]
    [InlineData("{\"a\":1,}")]
    [InlineData("// note\n1")]
    [InlineData("\"\\uD800\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("{\"\\uD800\":1}")]
    public void Text_that_is_not_one_json_value_is_refused(string? text)
    {
        Assert.False(FlagValue.TryParse(text, out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Text_with_a_lone_surrogate_is_refused() =>
        Assert.False(FlagValue.TryParse("\"\uD800\"", out _));

    [Fact]
    public void Depth_is_left_for_the_api_to_judge()
    {
        var text = new string('[', 100) + new string(']', 100);

        Assert.True(FlagValue.TryParse(text, out _));
    }

    [Fact]
    public void The_same_json_written_differently_is_the_same_value() =>
        Assert.Equal(Values.Json("{ \"a\": [1, 2] }"), Values.Json("{\"a\":[1,2]}"));

    [Fact]
    public void Markup_stays_as_written_for_the_page_to_encode() =>
        Assert.Equal("\"<b>&</b>\"", Values.Json("\"<b>&</b>\"").Text);

    [Fact]
    public void Indented_puts_one_key_per_line_with_two_spaces()
    {
        var value = Values.Json("""{"discount":0.2,"items":["sword"],"extra":{}}""");

        Assert.Equal("{\n  \"discount\": 0.2,\n  \"items\": [\n    \"sword\"\n  ],\n  \"extra\": {}\n}", value.Indented());
    }

    [Theory]
    [InlineData("true", "true")]
    [InlineData("\"gold\"", "\"gold\"")]
    [InlineData("1.50", "1.50")]
    public void A_scalar_is_already_indented(string json, string expected) =>
        Assert.Equal(expected, Values.Json(json).Indented());

    [Theory]
    [InlineData("12345", 5, "12345")]
    [InlineData("123456", 5, "1234…")]
    [InlineData("{\"discount\":0.2,\"featuredItems\":[\"sword\",\"shield\"]}", 32, "{\"discount\":0.2,\"featuredItems\"…")]
    public void A_preview_cuts_long_values_with_an_ellipsis(string json, int maxLength, string expected) =>
        Assert.Equal(expected, Values.Json(json).Preview(maxLength));

    [Theory]
    [InlineData("\"ab\\u2028cd\"", 6, "\"ab…")]
    [InlineData("\"ab\\u2028cd\"", 10, "\"ab\\u2028…")]
    [InlineData("\"a\\\\b\"", 4, "\"a…")]
    public void A_preview_never_cuts_an_escape_in_half(string json, int maxLength, string expected) =>
        Assert.Equal(expected, Values.Json(json).Preview(maxLength));

    [Fact]
    public void The_shortest_preview_is_one_character_and_the_ellipsis() =>
        Assert.Equal("1…", Values.Json("123").Preview(2));

    [Fact]
    public void A_preview_without_room_for_a_character_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Values.Json("123").Preview(1));

    [Theory]
    [InlineData("null", true)]
    [InlineData("\"null\"", false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    public void Only_the_json_null_is_null(string json, bool expected) =>
        Assert.Equal(expected, Values.Json(json).IsNull);

    [Fact]
    public void Booleans_are_the_json_literals()
    {
        Assert.Equal("true", FlagValue.True.Text);
        Assert.Equal("false", FlagValue.False.Text);
    }
}

internal sealed record ValueHolder(FlagValue Value);
