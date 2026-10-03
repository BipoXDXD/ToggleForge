using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>O último uso da credencial, gravado no máximo uma vez a cada 5 minutos para o polling não virar escrita.</summary>
public sealed class CredentialUsageTests(ApiFactory api)
{
    private static readonly DateTimeOffset _start = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_credential_the_game_never_used_has_no_last_use()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.CreateProjectAsync("space-race");

        var created = await owner.CreateCredentialAsync("space-race");
        var listed = await owner.ListCredentialsAsync("space-race");

        Assert.Null(created.Json["lastUsedAt"]);
        Assert.Null(Assert.Single(listed.Json["data"]!.AsArray())!["lastUsedAt"]);
    }

    [Fact]
    public async Task Downloading_the_snapshot_records_the_last_use()
    {
        var clock = new FakeTimeProvider(_start);
        await using var clocked = WithClock(clock);
        using var owner = await ConsoleClient.SignUpAsync(clocked);
        var secret = await PublishedCredentialAsync(owner);
        clock.Advance(TimeSpan.FromSeconds(30));

        await GameClient.GetSnapshotAsync(clocked, secret);

        Assert.Equal(_start.AddSeconds(30), await LastUsedAtAsync(owner));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task A_later_use_is_recorded_only_after_5_minutes(int microsecondsLater, bool recorded)
    {
        var clock = new FakeTimeProvider(_start);
        await using var clocked = WithClock(clock);
        using var owner = await ConsoleClient.SignUpAsync(clocked);
        var secret = await PublishedCredentialAsync(owner);
        await GameClient.GetSnapshotAsync(clocked, secret);
        var later = _start + TimeSpan.FromMinutes(5) + TimeSpan.FromMicroseconds(microsecondsLater);
        clock.SetUtcNow(later);

        await GameClient.GetSnapshotAsync(clocked, secret);

        Assert.Equal(recorded ? later : _start, await LastUsedAtAsync(owner));
    }

    [Fact]
    public async Task Using_one_credential_leaves_the_others_untouched()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var used = (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
        var unusedId = (await owner.CreateCredentialAsync("space-race")).Json["id"]!.GetValue<string>();

        await GameClient.GetSnapshotAsync(api, used);

        var unused = (await owner.ListCredentialsAsync("space-race")).Json["data"]!.AsArray()
            .Single(credential => credential!["id"]!.GetValue<string>() == unusedId)!;
        Assert.Null(unused["lastUsedAt"]);
    }

    private WebApplicationFactory<Program> WithClock(TimeProvider clock) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(clock)));

    private static async Task<string> PublishedCredentialAsync(ConsoleClient owner)
    {
        await owner.PublishedProjectAsync("space-race");
        return (await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>();
    }

    private static async Task<DateTimeOffset?> LastUsedAtAsync(ConsoleClient owner) =>
        Assert.Single((await owner.ListCredentialsAsync("space-race")).Json["data"]!.AsArray())!["lastUsedAt"]?.GetValue<DateTimeOffset>();
}
