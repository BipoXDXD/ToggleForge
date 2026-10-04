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
}
