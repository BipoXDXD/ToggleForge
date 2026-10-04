using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class RollbackTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _editor = Guid.NewGuid();

    [Fact]
    public void Rolling_back_publishes_the_target_content_as_the_next_version()
    {
        var (environment, first, second) = TwoPublications();

        var rollback = RollBack(environment, first, second);

        Assert.Equal(3, rollback.Version);
        Assert.Equal(3, environment.CurrentVersion);
        Assert.Equal(PublicationOrigin.Rollback, rollback.Origin);
        Assert.Equal(1, rollback.RestoredVersion);
        Assert.Null(rollback.Summary);
        Assert.Equal(_editor, rollback.PublishedBy);
        Assert.Equal(_now, rollback.PublishedAt);
        Assert.Equal(environment.Id, rollback.EnvironmentId);
        Assert.Equal(first.Flags, rollback.Flags);
    }

    [Fact]
    public void Rolling_back_replaces_the_draft_with_the_target_content_in_a_new_revision()
    {
        var (environment, first, second) = TwoPublications();
        var revisionBefore = environment.DraftRevision;

        var rollback = RollBack(environment, first, second);

        Assert.Equal(revisionBefore + 1, environment.DraftRevision);
        Assert.Equal(first.Flags, environment.Flags.Select(flag => flag.ToPublished()));
        Assert.All(environment.Flags, flag => Assert.Equal(environment.DraftRevision, flag.ChangedAtRevision));
        Assert.Empty(environment.PendingChanges(rollback));
    }

    [Fact]
    public void Rolling_back_restores_the_seed_of_a_flag_recreated_after_the_target()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        var first = Publish(environment, current: null);
        Remove(environment, "new-shop");
        Assert.True(environment.AddFlag(FlagKey.Parse("new-shop"), Flags.Settings(), RolloutSeed.Parse("seed-recreated"), PublishedFlagTypes.None).TryGetValue(out _, out _));
        var second = Publish(environment, first);

        RollBack(environment, first, second);

        Assert.Equal(first.Flags[0].Seed, environment.Flags.Single().Seed);
    }

    [Fact]
    public void Rolling_back_to_an_unknown_version_fails_and_changes_nothing()
    {
        var (environment, _, second) = TwoPublications();
        var revisionBefore = environment.DraftRevision;

        var rolledBack = environment.RollBack(target: null, expectedCurrentVersion: 2, discardDraftChanges: false, _editor, _now, second, PublishedFlagTypes.None);

        AssertFails(AuthoringFailures.RollbackTargetNotFound, rolledBack);
        Assert.Equal(2, environment.CurrentVersion);
        Assert.Equal(revisionBefore, environment.DraftRevision);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Rolling_back_after_another_version_than_the_one_seen_fails(long expectedCurrentVersion)
    {
        var (environment, first, second) = TwoPublications();

        var rolledBack = environment.RollBack(first, expectedCurrentVersion, discardDraftChanges: false, _editor, _now, second, PublishedFlagTypes.None);

        AssertFails(AuthoringFailures.CurrentVersionChanged, rolledBack);
        Assert.Equal(2, environment.CurrentVersion);
    }

    [Fact]
    public void Rolling_back_to_the_current_version_fails()
    {
        var (environment, _, second) = TwoPublications();

        var rolledBack = environment.RollBack(second, expectedCurrentVersion: 2, discardDraftChanges: false, _editor, _now, second, PublishedFlagTypes.None);

        AssertFails(AuthoringFailures.AlreadyCurrent, rolledBack);
    }

    [Fact]
    public void Rolling_back_to_a_version_with_the_current_content_fails()
    {
        var (environment, first, second) = TwoPublications();
        var third = RollBack(environment, first, second);

        var rolledBack = environment.RollBack(first, expectedCurrentVersion: 3, discardDraftChanges: false, _editor, _now, third, PublishedFlagTypes.None);

        AssertFails(AuthoringFailures.AlreadyCurrent, rolledBack);
        Assert.Equal(3, environment.CurrentVersion);
    }

    [Fact]
    public void Rolling_back_over_unpublished_changes_fails_and_keeps_them()
    {
        var (environment, first, second) = TwoPublications();
        Flags.Add(environment, "season-event");
        var revisionBefore = environment.DraftRevision;

        var rolledBack = environment.RollBack(first, expectedCurrentVersion: 2, discardDraftChanges: false, _editor, _now, second, PublishedFlagTypes.None);

        AssertFails(AuthoringFailures.DraftHasPendingChanges, rolledBack);
        Assert.Equal(revisionBefore, environment.DraftRevision);
        Assert.Contains(environment.Flags, flag => flag.Key.Value == "season-event");
    }

    [Fact]
    public void Rolling_back_with_discard_replaces_the_unpublished_changes()
    {
        var (environment, first, second) = TwoPublications();
        Flags.Add(environment, "season-event");

        var rollback = RollBack(environment, first, second, discardDraftChanges: true);

        Assert.Equal(3, rollback.Version);
        Assert.Equal(first.Flags, environment.Flags.Select(flag => flag.ToPublished()));
    }

    [Fact]
    public void Rolling_back_against_a_publication_that_is_not_in_force_is_a_programming_error()
    {
        var (environment, first, _) = TwoPublications();

        Assert.Throws<InvalidOperationException>(
            () => environment.RollBack(first, expectedCurrentVersion: 2, discardDraftChanges: false, _editor, _now, first, PublishedFlagTypes.None));
    }

    [Fact]
    public void Rolling_back_to_a_publication_of_another_environment_is_a_programming_error()
    {
        var (environment, _, second) = TwoPublications();
        var (_, otherFirst, _) = TwoPublications();

        Assert.Throws<InvalidOperationException>(
            () => environment.RollBack(otherFirst, expectedCurrentVersion: 2, discardDraftChanges: false, _editor, _now, second, PublishedFlagTypes.None));
    }

    /// <summary>Versão 1 com <c>new-shop</c> a 10%; versão 2 com o rollout em 50% e <c>battle-pass</c>.</summary>
    private static (ProjectEnvironment Environment, Publication First, Publication Second) TwoPublications()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop", basisPoints: 1_000);
        var first = Publish(environment, current: null);
        var flag = environment.Flags.Single();
        Assert.True(environment.ChangeFlag(flag.Key, flag.ChangedAtRevision, new FlagChange(Rollout: RolloutPercentage.FromBasisPoints(5_000)))
            .TryGetValue(out _, out _));
        Flags.Add(environment, "battle-pass");
        var second = Publish(environment, first);
        return (environment, first, second);
    }

    private static Publication RollBack(
        ProjectEnvironment environment, Publication target, Publication current, bool discardDraftChanges = false)
    {
        var rolledBack = environment.RollBack(target, current.Version, discardDraftChanges, _editor, _now, current, PublishedFlagTypes.None);
        Assert.True(rolledBack.TryGetValue(out var rollback, out var failure), failure?.Detail);
        return rollback;
    }

    private static Publication Publish(ProjectEnvironment environment, Publication? current)
    {
        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current, PublishedFlagTypes.None);
        Assert.True(published.TryGetValue(out var publication, out var failure), failure?.Detail);
        return publication;
    }

    private static void Remove(ProjectEnvironment environment, string key)
    {
        var flag = environment.Flags.Single(flag => flag.Key.Value == key);
        Assert.True(environment.RemoveFlag(flag.Key, flag.ChangedAtRevision).TryGetValue(out _, out _));
    }

    private static void AssertFails(Failure expected, Result<Publication> result)
    {
        Assert.False(result.TryGetValue(out _, out var failure));
        Assert.Equal(expected, failure);
    }
}
