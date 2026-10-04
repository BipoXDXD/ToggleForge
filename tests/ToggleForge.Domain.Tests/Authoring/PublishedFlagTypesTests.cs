using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

/// <summary>Uma chave publicada num ambiente mantém o tipo em toda publicação seguinte (ADR 0011).</summary>
public sealed class PublishedFlagTypesTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _editor = Guid.NewGuid();
    private static readonly FlagKey _shop = FlagKey.Parse("shop");

    [Theory]
    [InlineData(FlagType.Boolean, "boolean")]
    [InlineData(FlagType.Json, "json")]
    public void Adding_a_flag_with_another_type_than_its_key_was_published_with_fails_and_changes_nothing(
        FlagType publishedType, string typeName)
    {
        var environment = Flags.NewEnvironment();
        var revisionBefore = environment.DraftRevision;

        var added = environment.AddFlag(_shop, SettingsOf(Other(publishedType)), RolloutSeed.Parse("s1"), PublishedAs(publishedType));

        AssertTypeLocked(added.TryGetValue(out _, out var failure), failure, typeName);
        Assert.Empty(environment.Flags);
        Assert.Equal(revisionBefore, environment.DraftRevision);
    }

    [Theory]
    [InlineData(FlagType.Boolean)]
    [InlineData(FlagType.Json)]
    public void Adding_a_flag_with_the_type_its_key_was_published_with_succeeds(FlagType publishedType)
    {
        var environment = Flags.NewEnvironment();

        var added = environment.AddFlag(_shop, SettingsOf(publishedType), RolloutSeed.Parse("s1"), PublishedAs(publishedType));

        Assert.True(added.TryGetValue(out var flag, out var failure), failure?.Detail);
        Assert.Equal(publishedType, flag.Type);
    }

    [Fact]
    public void Another_key_is_free_to_take_any_type()
    {
        var environment = Flags.NewEnvironment();

        var added = environment.AddFlag(
            FlagKey.Parse("shop-v2"), SettingsOf(FlagType.Json), RolloutSeed.Parse("s1"), PublishedAs(FlagType.Boolean));

        Assert.True(added.TryGetValue(out _, out var failure), failure?.Detail);
    }

    [Theory]
    [InlineData(FlagType.Boolean, "boolean")]
    [InlineData(FlagType.Json, "json")]
    public void Publishing_a_key_with_another_type_than_published_fails_and_keeps_the_current_version(
        FlagType publishedType, string typeName)
    {
        var environment = Flags.NewEnvironment();
        Assert.True(environment.AddFlag(_shop, SettingsOf(Other(publishedType)), RolloutSeed.Parse("s1"), PublishedFlagTypes.None)
            .TryGetValue(out _, out _));

        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current: null, PublishedAs(publishedType));

        AssertTypeLocked(published.TryGetValue(out _, out var failure), failure, typeName);
        Assert.Null(environment.CurrentVersion);
    }

    [Fact]
    public void Publishing_a_key_with_the_type_it_was_published_with_succeeds()
    {
        var environment = Flags.NewEnvironment();
        Assert.True(environment.AddFlag(_shop, SettingsOf(FlagType.Json), RolloutSeed.Parse("s1"), PublishedFlagTypes.None)
            .TryGetValue(out _, out _));

        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current: null, PublishedAs(FlagType.Json));

        Assert.True(published.TryGetValue(out var publication, out var failure), failure?.Detail);
        Assert.Equal(FlagType.Json, publication.Flags.Single().Settings.Type);
    }

    [Fact]
    public void Rolling_back_to_a_key_with_another_type_than_published_fails_and_keeps_the_draft()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "shop");
        var first = Publish(environment, current: null);
        var flag = environment.Flags.Single();
        Assert.True(environment.ChangeFlag(flag.Key, flag.ChangedAtRevision, new FlagChange(Rollout: RolloutPercentage.All)).TryGetValue(out _, out _));
        var second = Publish(environment, first);
        var draftBefore = environment.Flags.Select(draftFlag => draftFlag.ToPublished()).ToList();
        var revisionBefore = environment.DraftRevision;

        var rolledBack = environment.RollBack(
            first, second.Version, discardDraftChanges: false, _editor, _now, second, PublishedAs(FlagType.Json));

        AssertTypeLocked(rolledBack.TryGetValue(out _, out var failure), failure, "json");
        Assert.Equal(2, environment.CurrentVersion);
        Assert.Equal(revisionBefore, environment.DraftRevision);
        Assert.Equal(draftBefore, environment.Flags.Select(draftFlag => draftFlag.ToPublished()));
    }

    private static PublishedFlagTypes PublishedAs(FlagType type) => new([new(_shop, type)]);

    private static FlagType Other(FlagType type) => type == FlagType.Boolean ? FlagType.Json : FlagType.Boolean;

    private static FlagSettings SettingsOf(FlagType type) =>
        type == FlagType.Boolean
            ? Flags.Settings()
            : new FlagSettings(IsEnabled: true, new JsonValues(JsonFlagValue.Parse("{\"a\":1}"), JsonFlagValue.Parse("null")), RolloutPercentage.All);

    private static Publication Publish(ProjectEnvironment environment, Publication? current)
    {
        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current, PublishedAs(FlagType.Boolean));
        Assert.True(published.TryGetValue(out var publication, out var failure), failure?.Detail);
        return publication;
    }

    private static void AssertTypeLocked(bool succeeded, Failure? failure, string publishedTypeName)
    {
        Assert.False(succeeded);
        Assert.NotNull(failure);
        Assert.Equal((FailureKind.Conflict, "flag-type-locked"), (failure.Kind, failure.Code));
        Assert.Equal(
            $"The key 'shop' was already published in this environment as a {publishedTypeName} flag, and a published key keeps its type. Use a new key for a flag of another type.",
            failure.Detail);
    }
}
