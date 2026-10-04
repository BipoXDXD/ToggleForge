namespace ToggleForge.Evaluation.Tests;

public sealed class ConfigurationSnapshotTests
{
    [Fact]
    public void TryGetFlag_finds_a_published_flag_by_key()
    {
        var newShop = Flag("new-shop");
        var snapshot = SnapshotWith(newShop, Flag("battle-pass"));

        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out var found));
        Assert.Same(newShop, found);
    }

    [Fact]
    public void TryGetFlag_reports_an_unknown_key()
    {
        var snapshot = SnapshotWith(Flag("new-shop"));

        Assert.False(snapshot.TryGetFlag(FlagKey.Parse("battle-pass"), out var found));
        Assert.Null(found);
    }

    [Fact]
    public void Rejects_duplicate_flag_keys()
    {
        var duplicate = Flag("new-shop");

        var error = Assert.Throws<ArgumentException>(() => SnapshotWith(duplicate, duplicate));

        Assert.Equal("flags", error.ParamName);
    }

    [Fact]
    public void Rejects_the_same_key_in_flags_of_different_types()
    {
        var config = new JsonFlag(
            FlagKey.Parse("new-shop"), true, "{}", "{}", new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

        var error = Assert.Throws<ArgumentException>(() => SnapshotWith(Flag("new-shop"), config));

        Assert.Equal("flags", error.ParamName);
    }

    [Fact]
    public void Rejects_null_flags_in_the_list()
    {
        var error = Assert.Throws<ArgumentException>(() => SnapshotWith([null!]));

        Assert.Equal("flags", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_versions_below_1(long version)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConfigurationSnapshot(NewShopFlag.Project, NewShopFlag.Environment, version, []));

        Assert.Equal("version", error.ParamName);
    }

    [Fact]
    public void Keeps_its_flags_when_the_source_list_changes()
    {
        var flags = new List<BooleanFlag> { Flag("new-shop") };
        var snapshot = new ConfigurationSnapshot(NewShopFlag.Project, NewShopFlag.Environment, 1, flags);

        flags.Clear();

        Assert.True(snapshot.TryGetFlag(FlagKey.Parse("new-shop"), out _));
    }

    private static BooleanFlag Flag(string key) =>
        new(
            FlagKey.Parse(key),
            isEnabled: true,
            onValue: true,
            offValue: false,
            new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

    private static ConfigurationSnapshot SnapshotWith(params Flag[] flags) =>
        new(NewShopFlag.Project, NewShopFlag.Environment, 1, flags);
}
