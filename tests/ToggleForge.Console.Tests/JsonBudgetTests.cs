using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class JsonBudgetTests
{
    [Fact]
    public void The_budget_adds_up_the_utf8_bytes_of_both_values_of_every_json_flag()
    {
        Flag[] flags =
        [
            new("dark-mode", FlagTypes.Boolean, Enabled: true, FlagValue.True, FlagValue.False, RolloutBasisPoints: 0),
            new("shop-config", FlagTypes.Json, Enabled: true,
                Values.Json("""{"discount":0.2,"featuredItems":["sword","shield"]}"""), Values.Json("""{"discount":0}"""), RolloutBasisPoints: 10_000),
            new("greeting", FlagTypes.Json, Enabled: false, Values.Json("\"ção\""), FlagValue.Null, RolloutBasisPoints: 0),
        ];

        // 51 + 14 bytes da shop-config; "ção" tem 5 caracteres e 7 bytes, mais 4 do null. A booleana não conta.
        Assert.Equal(76, JsonBudget.BytesOf(flags));
    }

    [Fact]
    public void A_draft_without_json_flags_spends_nothing() =>
        Assert.Equal(0, JsonBudget.BytesOf([new Flag("dark-mode", FlagTypes.Boolean, Enabled: true, FlagValue.True, FlagValue.False, 0)]));

    [Theory]
    [InlineData(0, "0.0")]
    [InlineData(103, "0.1")]
    [InlineData(1_023, "0.9")]
    [InlineData(1_024, "1.0")]
    [InlineData(3_994, "3.9")]
    [InlineData(131_071, "127.9")]
    [InlineData(131_072, "128.0")]
    public void The_spent_bytes_read_as_kibibytes_rounded_down(int bytes, string expected) =>
        Assert.Equal(expected, JsonBudget.FormatKibibytes(bytes));

    // 0,1 KiB são 102,4 bytes: de 1 a 102 o arredondamento para baixo mostraria 0.0, como um rascunho sem nada.
    [Theory]
    [InlineData(1)]
    [InlineData(102)]
    public void Less_than_a_tenth_of_a_kibibyte_does_not_read_as_empty(int bytes) =>
        Assert.Equal("< 0.1", JsonBudget.FormatKibibytes(bytes));

    [Theory]
    [InlineData(98_304, false)]
    [InlineData(98_305, true)]
    public void Above_three_quarters_of_the_budget_the_draft_is_near_the_limit(int bytes, bool expected) =>
        Assert.Equal(expected, JsonBudget.IsNearLimit(bytes));
}
