using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class PublishingTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _editor = Guid.NewGuid();

    [Fact]
    public void First_publication_is_version_1_with_the_draft_content_ordered_by_key()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop", basisPoints: 500);
        Flags.Add(environment, "battle-pass", basisPoints: 10_000);
        Assert.True(ChangeSummary.TryParse("Shop for 5%", out var summary));

        var publication = Publish(environment, current: null, summary);

        Assert.Equal(1, publication.Version);
        Assert.Equal(1, environment.CurrentVersion);
        Assert.Equal(environment.Id, publication.EnvironmentId);
        Assert.Equal(_editor, publication.PublishedBy);
        Assert.Equal(_now, publication.PublishedAt);
        Assert.Equal(PublicationOrigin.Publication, publication.Origin);
        Assert.Null(publication.RestoredVersion);
        Assert.Equal(summary, publication.Summary);
        Assert.Equal(["battle-pass", "new-shop"], publication.Flags.Select(flag => flag.Key.Value));
        Assert.Equal(environment.Flags.Single(flag => flag.Key.Value == "new-shop").ToPublished(), publication.Flags[1]);
    }

    [Fact]
    public void Next_publication_increments_the_version()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        var first = Publish(environment, current: null);
        Flags.Add(environment, "battle-pass");

        var second = Publish(environment, first);

        Assert.Equal(2, second.Version);
        Assert.Equal(2, environment.CurrentVersion);
        Assert.Equal(2, second.Flags.Count);
    }

    [Fact]
    public void Publishing_a_stale_revision_fails_and_keeps_the_current_version()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        var reviewedRevision = environment.DraftRevision;
        Flags.Add(environment, "battle-pass");

        var published = environment.Publish(reviewedRevision, summary: null, _editor, _now, current: null);

        Assert.False(published.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.DraftChanged, failure);
        Assert.Null(environment.CurrentVersion);
    }

    [Fact]
    public void Publishing_an_empty_draft_for_the_first_time_has_nothing_to_publish()
    {
        var environment = Flags.NewEnvironment();

        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current: null);

        Assert.False(published.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.NothingToPublish, failure);
    }

    [Fact]
    public void Publishing_again_without_changes_has_nothing_to_publish()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        var first = Publish(environment, current: null);

        var published = environment.Publish(environment.DraftRevision, summary: null, _editor, _now, first);

        Assert.False(published.TryGetValue(out _, out var failure));
        Assert.Equal(AuthoringFailures.NothingToPublish, failure);
        Assert.Equal(1, environment.CurrentVersion);
    }

    [Fact]
    public void Publishing_against_a_publication_that_is_not_in_force_is_a_programming_error()
    {
        var environment = Flags.NewEnvironment();
        Flags.Add(environment, "new-shop");
        Publish(environment, current: null);

        Assert.Throws<InvalidOperationException>(
            () => environment.Publish(environment.DraftRevision, summary: null, _editor, _now, current: null));
    }

    private static Publication Publish(ProjectEnvironment environment, Publication? current, ChangeSummary? summary = null)
    {
        var published = environment.Publish(environment.DraftRevision, summary, _editor, _now, current);
        Assert.True(published.TryGetValue(out var publication, out var failure), failure?.Detail);
        return publication;
    }
}
