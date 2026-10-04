using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class JsonTextTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  \n\t ")]
    public void Blank_text_is_empty(string text) => Assert.IsType<JsonAnalysis.Empty>(JsonText.Analyze(text));

    [Theory]
    [InlineData("3", "3", 1)]
    [InlineData(" \"gold\" ", "\"gold\"", 6)]
    [InlineData("{ \"b\": 1,\n  \"a\": [1, 2] }", """{"b":1,"a":[1,2]}""", 17)]
    [InlineData("\"é<\"", "\"é<\"", 5)]
    public void A_value_is_compacted_in_key_order_and_counted_in_utf8_bytes(string text, string compact, int bytes)
    {
        var valid = Assert.IsType<JsonAnalysis.Valid>(JsonText.Analyze(text));

        Assert.Equal(compact, valid.Value.Text);
        Assert.Equal(bytes, valid.ByteCount);
    }

    [Fact]
    public void A_missing_comma_points_to_the_line_and_column_after_it()
    {
        var text = "{\n  \"discount\": 0.2\n  \"featuredItems\": [\"sword\"]\n}";

        var invalid = Assert.IsType<JsonAnalysis.Invalid>(JsonText.Analyze(text));

        Assert.Equal(new SyntaxPosition(3, 3, "  \"featuredItems\": [\"sword\"]"), invalid.Position);
    }

    [Fact]
    public void The_column_counts_characters_not_bytes()
    {
        var invalid = Assert.IsType<JsonAnalysis.Invalid>(JsonText.Analyze("\"ação\" x"));

        Assert.Equal(new SyntaxPosition(1, 8, "\"ação\" x"), invalid.Position);
    }

    [Theory]
    [InlineData("1 2")]
    [InlineData("[1,]")]
    [InlineData("// note\n1")]
    [InlineData("{\"a\": ")]
    [InlineData("nul")]
    public void Text_that_is_not_one_json_value_is_invalid(string text) =>
        Assert.NotNull(Assert.IsType<JsonAnalysis.Invalid>(JsonText.Analyze(text)).Position);

    [Fact]
    public void An_escape_for_half_a_character_is_invalid_without_a_position() =>
        Assert.Null(Assert.IsType<JsonAnalysis.Invalid>(JsonText.Analyze("\"\\uD800\"")).Position);

    [Fact]
    public void Nesting_past_what_the_console_reads_is_too_deep()
    {
        Assert.IsType<JsonAnalysis.Valid>(JsonText.Analyze(Nested(512)));
        Assert.IsType<JsonAnalysis.TooDeep>(JsonText.Analyze(Nested(513)));
    }

    [Theory]
    [InlineData(0, MeterLevel.Comfortable)]
    [InlineData(3_071, MeterLevel.Comfortable)]
    [InlineData(3_072, MeterLevel.NearLimit)]
    [InlineData(4_096, MeterLevel.NearLimit)]
    [InlineData(4_097, MeterLevel.OverLimit)]
    public void The_meter_turns_near_the_limit_at_three_quarters_and_over_it_past_4_KiB(int bytes, MeterLevel expected) =>
        Assert.Equal(expected, JsonText.LevelOf(bytes));

    [Theory]
    [InlineData(2_047, 4_096)]
    [InlineData(2_048, 4_098)]
    public void Multibyte_characters_count_twice_toward_the_limit(int accents, int bytes)
    {
        var valid = Assert.IsType<JsonAnalysis.Valid>(JsonText.Analyze($"\"{new string('é', accents)}\""));

        Assert.Equal(bytes, valid.ByteCount);
    }

    [Theory]
    [InlineData("  \"a\" x", 7, "      ^")]
    [InlineData("\t\"a\" x", 6, "\t    ^")]
    [InlineData("x", 1, "^")]
    public void The_caret_sits_under_the_column_keeping_the_tabs_of_the_line(string line, int column, string caret) =>
        Assert.Equal(caret, new SyntaxPosition(1, column, line).Caret);

    private static string Nested(int levels) => new string('[', levels) + new string(']', levels);
}
