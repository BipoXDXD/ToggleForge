using ToggleForge.Evaluation;

namespace ToggleForge.Sdk.Tests;

public sealed class SnapshotReaderTests
{
    [Fact]
    public void A_snapshot_from_the_api_becomes_the_configuration_the_game_evaluates()
    {
        var snapshot = SnapshotReader.Read(Snapshots.Json(version: 7));

        Assert.NotNull(snapshot);
        Assert.Equal(("space-race", "dev", 7L), (snapshot.ProjectKey.Value, snapshot.EnvironmentKey.Value, snapshot.Version));
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out var found));
        var flag = Assert.IsType<BooleanFlag>(found);
        Assert.Equal((true, true, false), (flag.IsEnabled, flag.OnValue, flag.OffValue));
        var rollout = Assert.IsType<PercentageRollout>(flag.Targeting);
        Assert.Equal((10_000, "s1"), (rollout.Percentage.BasisPoints, rollout.Seed.Value));
    }

    [Fact]
    public void Fields_this_sdk_does_not_know_are_ignored() =>
        Assert.NotNull(SnapshotReader.Read(Snapshots.Json(flags: """[{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":0,"seed":"s1","segments":[]}]""")
            .Replace("\"flags\"", "\"schedule\":{\"at\":null},\"flags\"", StringComparison.Ordinal)));

    [Fact]
    public void A_snapshot_without_flags_is_valid() =>
        Assert.NotNull(SnapshotReader.Read(Snapshots.Json(flags: "[]")));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"algorithm":"toggleforge.rollout.v1"} trailing""")]
    public void A_body_that_is_not_a_json_object_is_refused(string body) =>
        Assert.Null(SnapshotReader.Read(body));

    [Theory]
    [InlineData("\"algorithm\":\"toggleforge.rollout.v1\"", "\"algorithm\":\"toggleforge.rollout.v2\"")]
    [InlineData("\"projectKey\":\"space-race\"", "\"projectKey\":\"Space Race\"")]
    [InlineData("\"environmentKey\":\"dev\"", "\"environmentKey\":7")]
    [InlineData("\"version\":1", "\"version\":0")]
    [InlineData("\"version\":1", "\"version\":\"1\"")]
    [InlineData("\"version\":1", "\"version\":1.0")]
    [InlineData("\"version\":1", "\"version\":99999999999999999999")]
    [InlineData("\"version\":1", "\"version\":1,\"version\":2")]
    public void A_wrong_field_of_the_snapshot_refuses_all_of_it(string field, string replacement) =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json().Replace(field, replacement, StringComparison.Ordinal)));

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1]")]
    public void Flags_that_are_not_a_list_of_objects_refuse_the_snapshot(string flags) =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: flags)));

    [Theory]
    [InlineData("\"key\":\"new-shop\"", "\"key\":\"New Shop\"")]
    [InlineData("\"enabled\":true", "\"enabled\":\"true\"")]
    [InlineData("\"onValue\":true", "\"onValue\":1")]
    [InlineData("\"offValue\":false", "\"offValue\":null")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":10001")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":-1")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":5000.5")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":4294967296")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":-4294967296")]
    [InlineData("\"seed\":\"s1\"", "\"seed\":\"  \"")]
    [InlineData(",\"seed\":\"s1\"", "")]
    public void One_wrong_flag_refuses_the_whole_snapshot(string field, string replacement) =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json().Replace(field, replacement, StringComparison.Ordinal)));

    [Fact]
    public void A_flag_key_repeated_in_the_list_refuses_the_snapshot()
    {
        const string flag = """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":0,"seed":"s1"}""";

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag},{flag}]")));
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void Json_nested_up_to_64_levels_is_read_and_deeper_is_refused(int listLevels, bool accepted)
    {
        // A raiz conta como o primeiro nível; as configurações JSON futuras precisam de folga (ADR 0010).
        var nested = new string('[', listLevels) + new string(']', listLevels);

        var snapshot = SnapshotReader.Read(Snapshots.Json().Replace("\"flags\"", $"\"extra\":{nested},\"flags\"", StringComparison.Ordinal));

        Assert.Equal(accepted, snapshot is not null);
    }

    [Theory]
    [InlineData("1e300")]
    [InlineData("-1e300")]
    [InlineData("0.1")]
    public void A_number_of_any_size_in_an_unknown_field_is_ignored(string number) =>
        Assert.NotNull(SnapshotReader.Read(Snapshots.Json().Replace("\"flags\"", $"\"extra\":{number},\"flags\"", StringComparison.Ordinal)));

    [Fact]
    public void A_seed_with_a_control_character_refuses_the_snapshot() =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json().Replace("\"seed\":\"s1\"", "\"seed\":\"\\u0000\"", StringComparison.Ordinal)));

    [Theory]
    [InlineData(" {}")]
    [InlineData(" trailing")]
    public void Content_after_the_snapshot_refuses_it(string trailing) =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json() + trailing));

    [Fact]
    public void Text_that_looks_like_a_date_is_read_as_written()
    {
        var snapshot = SnapshotReader.Read(Snapshots.Json().Replace("\"seed\":\"s1\"", "\"seed\":\"2026-10-04T12:00:00Z\"", StringComparison.Ordinal));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out var flag));
        Assert.Equal("2026-10-04T12:00:00Z", Assert.IsType<PercentageRollout>(flag.Targeting).Seed.Value);
    }

    // Um tipo que nenhum SDK conhece hoje, com campos que uma flag booleana recusaria.
    private const string NewerTypeFlag =
        """{"key":"shop-color","type":"color","enabled":true,"onValue":"#ff0000","offValue":{"r":0},"seed":"s2"}""";

    private const string BooleanFlagJson =
        """{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":10000,"seed":"s1"}""";

    [Fact]
    public void A_flag_of_an_unknown_type_is_skipped_and_the_rest_still_applies()
    {
        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{NewerTypeFlag},{BooleanFlagJson}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("shop-color"), out _));
    }

    [Fact]
    public void A_flag_may_state_that_it_is_boolean()
    {
        var typed = BooleanFlagJson.Replace("\"enabled\"", "\"type\":\"boolean\",\"enabled\"", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{typed}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
    }

    [Fact]
    public void A_type_is_matched_exactly_so_another_casing_is_an_unknown_type()
    {
        var shouting = BooleanFlagJson.Replace("\"enabled\"", "\"type\":\"Boolean\",\"enabled\"", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{shouting}]"));

        Assert.NotNull(snapshot);
        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void A_type_that_is_not_text_refuses_the_snapshot(string type)
    {
        var flag = BooleanFlagJson.Replace("\"enabled\"", $"\"type\":{type},\"enabled\"", StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag}]")));
    }

    [Fact]
    public void A_key_repeated_by_a_flag_of_an_unknown_type_refuses_the_snapshot()
    {
        var clash = NewerTypeFlag.Replace("shop-color", "new-shop", StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{clash},{BooleanFlagJson}]")));
    }

    [Theory]
    [InlineData("\"key\":\"Shop Color\",")]
    [InlineData("\"key\":7,")]
    [InlineData("")]
    public void A_flag_of_an_unknown_type_still_needs_a_valid_key(string key)
    {
        var flag = NewerTypeFlag.Replace("\"key\":\"shop-color\",", key, StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag},{BooleanFlagJson}]")));
    }

    [Fact]
    public void A_json_flag_is_read_with_its_values_compact_and_its_keys_in_order()
    {
        var spaced = Snapshots.JsonFlag(onValue: "{ \"discount\" : 0.2 ,\n \"featuredItems\" : [ \"sword\", \"shield\" ] }");

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{spaced}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("shop-config"), out var found));
        var flag = Assert.IsType<JsonFlag>(found);
        Assert.Equal(
            (true, """{"discount":0.2,"featuredItems":["sword","shield"]}""", "null"),
            (flag.IsEnabled, flag.OnValue, flag.OffValue));
        var rollout = Assert.IsType<PercentageRollout>(flag.Targeting);
        Assert.Equal((10_000, "s2"), (rollout.Percentage.BasisPoints, rollout.Seed.Value));
    }

    [Theory]
    [InlineData("\"2026-10-04T12:00:00Z\"")]
    [InlineData("\"2026-10-04T12:00:00+03:00\"")]
    [InlineData("12345678901234567890")]
    [InlineData("9007199254740993")]
    [InlineData("-7")]
    [InlineData("0.1")]
    [InlineData("1.0")]
    [InlineData("{\"b\":1,\"a\":[2,{}]}")]
    [InlineData("\"<script>ação\"")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"a\\\"b\\\\c\\n\"")]
    [InlineData("\"\\u0000\"")]
    public void Dates_integers_and_text_in_a_json_value_reach_the_game_as_written(string value) =>
        Assert.Equal(value, OnValueOf(Snapshots.JsonFlag(onValue: value)));

    [Theory]
    [InlineData("1.50", "1.5")]
    [InlineData("1e308", "1E+308")]
    [InlineData("1e-400", "0.0")]
    [InlineData("1e2", "100.0")]
    [InlineData("0.12345678901234567890123", "0.12345678901234568")]
    public void A_fraction_keeps_its_double_value_but_may_change_its_spelling(string value, string expected) =>
        Assert.Equal(expected, OnValueOf(Snapshots.JsonFlag(onValue: value)));

    [Fact]
    public void Lenient_json_inside_a_value_reaches_the_game_normalized() =>
        Assert.Equal(
            """{"a":"x","b":[1]}""",
            OnValueOf(Snapshots.JsonFlag(onValue: "{'a':'x', b:[1, /* note */],}")));

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("[1,1e400]")]
    [InlineData("NaN")]
    [InlineData("{\"a\":{\"b\":Infinity}}")]
    [InlineData("-Infinity")]
    [InlineData("undefined")]
    [InlineData("new Date(1)")]
    public void A_json_value_a_standard_parser_would_not_read_refuses_the_snapshot(string value) =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{Snapshots.JsonFlag(onValue: value)}]")));

    [Fact]
    public void An_off_value_a_standard_parser_would_not_read_refuses_the_snapshot_too() =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{Snapshots.JsonFlag(offValue: "NaN")}]")));

    [Fact]
    public void A_key_repeated_inside_a_json_value_refuses_the_snapshot() =>
        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{Snapshots.JsonFlag(onValue: "{\"a\":{\"b\":1,\"b\":2}}")}]")));

    [Theory]
    [InlineData("\"onValue\":{\"discount\":0.2,\"featuredItems\":[\"sword\"]},", "")]
    [InlineData(",\"offValue\":null", "")]
    [InlineData("\"enabled\":true", "\"enabled\":\"true\"")]
    [InlineData("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":10001")]
    [InlineData("\"seed\":\"s2\"", "\"seed\":\"  \"")]
    public void One_wrong_field_of_a_json_flag_refuses_the_snapshot(string field, string replacement)
    {
        var flag = Snapshots.JsonFlag().Replace(field, replacement, StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{flag}]")));
    }

    // As 8 camadas e os 4 KiB por valor são regras da API, que podem crescer; um SDK publicado não se corrige.
    [Fact]
    public void A_json_value_deeper_than_the_api_allows_is_still_read()
    {
        const string nineLevels = "[[[[[[[[[1]]]]]]]]]";

        Assert.Equal(nineLevels, OnValueOf(Snapshots.JsonFlag(onValue: nineLevels)));
    }

    [Fact]
    public void A_json_value_larger_than_the_api_allows_is_still_read()
    {
        var fiveThousandBytes = "\"" + new string('x', 4_998) + "\"";

        Assert.Equal(fiveThousandBytes, OnValueOf(Snapshots.JsonFlag(onValue: fiveThousandBytes)));
    }

    [Fact]
    public void A_json_flag_and_a_boolean_flag_cannot_share_a_key()
    {
        var clash = Snapshots.JsonFlag().Replace("shop-config", "new-shop", StringComparison.Ordinal);

        Assert.Null(SnapshotReader.Read(Snapshots.Json(flags: $"[{BooleanFlagJson},{clash}]")));
    }

    [Fact]
    public void A_disabled_json_flag_is_read_as_disabled()
    {
        var disabled = Snapshots.JsonFlag().Replace("\"enabled\":true", "\"enabled\":false", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{disabled}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("shop-config"), out var flag));
        Assert.False(Assert.IsType<JsonFlag>(flag).IsEnabled);
    }

    [Fact]
    public void The_json_type_is_matched_exactly_too()
    {
        var shouting = Snapshots.JsonFlag().Replace("\"type\":\"json\"", "\"type\":\"Json\"", StringComparison.Ordinal);

        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{shouting},{BooleanFlagJson}]"));

        Assert.NotNull(snapshot);
        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("shop-config"), out _));
    }

    private static string OnValueOf(string jsonFlag)
    {
        var snapshot = SnapshotReader.Read(Snapshots.Json(flags: $"[{jsonFlag}]"));

        Assert.NotNull(snapshot);
        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("shop-config"), out var flag));
        return Assert.IsType<JsonFlag>(flag).OnValue;
    }
}
