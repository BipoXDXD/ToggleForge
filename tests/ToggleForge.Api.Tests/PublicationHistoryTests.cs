using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class PublicationHistoryTests(ApiFactory api)
{
    [Fact]
    public async Task The_history_lists_every_version_newest_first_without_flags()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);
        await editor.RollBackAsync("space-race", 1, 2, ConsoleClient.NewIdempotencyKey());

        var history = await editor.ListPublicationsAsync("space-race");

        Assert.Equal(HttpStatusCode.OK, history.Status);
        Assert.Equal("private, no-cache", history.Header("Cache-Control"));
        Assert.Equal([3, 2, 1], Versions(history));
        var rollback = history.Json["data"]![0]!;
        Assert.Equal("rollback", rollback["origin"]!.GetValue<string>());
        Assert.Equal(1, rollback["restoredVersion"]!.GetValue<long>());
        Assert.Equal(editor.Email, rollback["publishedBy"]!["email"]!.GetValue<string>());
        Assert.Equal("Version 1", history.Json["data"]![2]!["summary"]!.GetValue<string>());
        Assert.All(history.Json["data"]!.AsArray(), item => Assert.Null(item!["flags"]));
        Assert.Null(history.Json["metadata"]!["nextPageToken"]);
    }

    [Fact]
    public async Task An_environment_that_never_published_has_an_empty_history()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");

        var history = await editor.ListPublicationsAsync("space-race");

        Assert.Equal(HttpStatusCode.OK, history.Status);
        Assert.Empty(history.Json["data"]!.AsArray());
        Assert.Null(history.Json["metadata"]!["nextPageToken"]);
    }

    [Theory]
    [InlineData(4, "4,3|2,1")]
    [InlineData(5, "5,4|3,2|1")]
    public async Task Pages_follow_the_token_until_the_last_page(int publications, string expectedPages)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, publications);

        var pages = await AllPagesAsync(editor, maxPageSize: 2);

        Assert.Equal(expectedPages, string.Join('|', pages.Select(page => string.Join(',', Versions(page)))));
        Assert.Null(pages[^1].Json["metadata"]!["nextPageToken"]);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("100")]
    public async Task The_page_size_accepts_1_to_100(string maxPageSize)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);

        var history = await editor.ListPublicationsAsync("space-race", $"maxPageSize={maxPageSize}");

        Assert.Equal(HttpStatusCode.OK, history.Status);
        Assert.Equal(Math.Min(2, int.Parse(maxPageSize, System.Globalization.CultureInfo.InvariantCulture)), Versions(history).Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("ten")]
    [InlineData("")]
    public async Task Other_page_sizes_are_rejected(string maxPageSize)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");

        var history = await editor.ListPublicationsAsync("space-race", $"maxPageSize={Uri.EscapeDataString(maxPageSize)}");

        Assert.Equal(HttpStatusCode.BadRequest, history.Status);
        Assert.Equal(["maxPageSize"], history.ErrorSources());
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("has spaces")]
    public async Task A_token_the_api_did_not_issue_is_rejected(string pageToken)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await editor.CreateProjectAsync("space-race");

        var history = await editor.ListPublicationsAsync("space-race", $"pageToken={Uri.EscapeDataString(pageToken)}");

        Assert.Equal(HttpStatusCode.BadRequest, history.Status);
        Assert.Equal(["pageToken"], history.ErrorSources());
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);
        var token = await FirstPageTokenAsync(editor);
        var tampered = token[..^5] + (token[^5] == 'A' ? 'B' : 'A') + token[^4..];

        var history = await editor.ListPublicationsAsync("space-race", $"maxPageSize=1&pageToken={tampered}");

        Assert.Equal(HttpStatusCode.BadRequest, history.Status);
        Assert.Equal(["pageToken"], history.ErrorSources());
    }

    [Fact]
    public async Task Another_member_cannot_use_the_token()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(owner, count: 2);
        using var viewer = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, viewer.MemberId, "viewer");
        var token = await FirstPageTokenAsync(owner);

        var history = await viewer.ListPublicationsAsync("space-race", $"maxPageSize=1&pageToken={token}", organizationId: owner.OrganizationId);

        Assert.Equal(HttpStatusCode.BadRequest, history.Status);
        Assert.Equal(["pageToken"], history.ErrorSources());
    }

    [Fact]
    public async Task The_token_of_one_environment_is_rejected_in_another()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);
        var token = await FirstPageTokenAsync(editor);

        var history = await editor.ListPublicationsAsync("space-race", $"maxPageSize=1&pageToken={token}", environmentKey: "prod");

        Assert.Equal(HttpStatusCode.BadRequest, history.Status);
        Assert.Equal(["pageToken"], history.ErrorSources());
    }

    [Theory]
    [InlineData(3599, HttpStatusCode.OK)]
    [InlineData(3600, HttpStatusCode.BadRequest)]
    public async Task The_token_lasts_60_minutes(int secondsLater, HttpStatusCode expected)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        await using var clocked = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<TimeProvider>(clock)));
        using var editor = await ConsoleClient.SignUpAsync(clocked);
        await PublishVersionsAsync(editor, count: 2);
        var token = await FirstPageTokenAsync(editor);

        clock.Advance(TimeSpan.FromSeconds(secondsLater));
        var history = await editor.ListPublicationsAsync("space-race", $"maxPageSize=1&pageToken={token}");

        Assert.Equal(expected, history.Status);
    }

    [Fact]
    public async Task Reading_a_publication_returns_its_flags()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);

        var publication = await editor.GetPublicationAsync("space-race", "1");

        Assert.Equal(HttpStatusCode.OK, publication.Status);
        Assert.Equal("private, no-cache", publication.Header("Cache-Control"));
        Assert.Equal(1, publication.Json["version"]!.GetValue<long>());
        Assert.Equal("Version 1", publication.Json["summary"]!.GetValue<string>());
        var flag = Assert.Single(publication.Json["flags"]!.AsArray())!;
        Assert.Equal(100, flag["rolloutBasisPoints"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 2);

        var publication = await editor.GetPublicationAsync("space-race", "3");

        Assert.Equal(HttpStatusCode.NotFound, publication.Status);
        Assert.Equal("/problems/not-found", publication.ProblemType);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("one")]
    [InlineData("99999999999999999999")]
    public async Task A_version_that_is_not_a_positive_integer_is_rejected(string version)
    {
        using var editor = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(editor, count: 1);

        var publication = await editor.GetPublicationAsync("space-race", Uri.EscapeDataString(version));

        Assert.Equal(HttpStatusCode.BadRequest, publication.Status);
        Assert.Equal(["version"], publication.ErrorSources());
    }

    [Fact]
    public async Task A_viewer_reads_the_history()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await PublishVersionsAsync(owner, count: 1);
        using var viewer = await ConsoleClient.SignUpAsync(api);
        await api.AddMembershipAsync(owner.OrganizationId, viewer.MemberId, "viewer");

        var history = await viewer.ListPublicationsAsync("space-race", organizationId: owner.OrganizationId);
        var publication = await viewer.GetPublicationAsync("space-race", "1", owner.OrganizationId);

        Assert.Equal([1], Versions(history));
        Assert.Equal(HttpStatusCode.OK, publication.Status);
    }

    /// <summary>Versão N com <c>new-shop</c> a N × 1%, resumo "Version N".</summary>
    private static async Task PublishVersionsAsync(ConsoleClient editor, int count)
    {
        Assert.Equal(HttpStatusCode.Created, (await editor.CreateProjectAsync("space-race")).Status);
        var flag = await editor.CreateFlagAsync("space-race", "new-shop", rolloutBasisPoints: 100);
        for (var version = 1; version <= count; version++)
        {
            if (version > 1)
            {
                flag = await editor.UpdateFlagAsync("space-race", "new-shop", flag.Header("ETag"), $$"""{"rolloutBasisPoints":{{version * 100}}}""");
            }

            var published = await editor.PublishAsync(
                "space-race", ConsoleClient.Revision(flag), ConsoleClient.NewIdempotencyKey(), summary: $"Version {version}");
            Assert.Equal(HttpStatusCode.Created, published.Status);
        }
    }

    private static async Task<List<ApiResponse>> AllPagesAsync(ConsoleClient editor, int maxPageSize)
    {
        var pages = new List<ApiResponse> { await editor.ListPublicationsAsync("space-race", $"maxPageSize={maxPageSize}") };
        while (pages[^1].Json["metadata"]!["nextPageToken"]?.GetValue<string>() is { } token)
        {
            pages.Add(await editor.ListPublicationsAsync("space-race", $"maxPageSize={maxPageSize}&pageToken={token}"));
        }

        return pages;
    }

    private static async Task<string> FirstPageTokenAsync(ConsoleClient editor) =>
        (await editor.ListPublicationsAsync("space-race", "maxPageSize=1")).Json["metadata"]!["nextPageToken"]!.GetValue<string>();

    private static List<long> Versions(ApiResponse page) =>
        page.Json["data"]!.AsArray().Select(item => item!["version"]!.GetValue<long>()).ToList();
}
