using System.Net;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

public sealed class SessionTests(ApiFactory api)
{
    private const int MaxFailedAttempts = 5;

    [Fact]
    public async Task Signing_up_starts_a_session_in_the_new_organization_as_owner()
    {
        using var member = await ConsoleClient.SignUpAsync(api);

        var session = await member.SendAsync("getSession", HttpMethod.Get, Paths.Session);

        Assert.Equal(HttpStatusCode.OK, session.Status);
        Assert.Equal(member.Email, session.Json["member"]!["email"]!.GetValue<string>());
        var organization = Assert.Single(session.Json["organizations"]!.AsArray())!;
        Assert.Equal(member.OrganizationId, Guid.Parse(organization["organizationId"]!.GetValue<string>()));
        Assert.Equal("owner", organization["role"]!.GetValue<string>());
    }

    [Fact]
    public async Task Signing_up_with_a_taken_email_conflicts()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        using var other = ConsoleClient.Anonymous(api);

        var response = await other.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new
        {
            email = member.Email.ToUpperInvariant(),
            password = ConsoleClient.Password,
            organizationName = "Copycat",
        });

        Assert.Equal(HttpStatusCode.Conflict, response.Status);
        Assert.Equal("/problems/email-taken", response.ProblemType);
    }

    [Theory]
    [InlineData(11, HttpStatusCode.BadRequest)]
    [InlineData(12, HttpStatusCode.Created)]
    public async Task New_passwords_need_at_least_12_characters(int length, HttpStatusCode expected)
    {
        using var client = ConsoleClient.Anonymous(api);

        var response = await client.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new
        {
            email = $"member-{Guid.NewGuid():N}@toggleforge.test",
            password = new string('p', length),
            organizationName = "Studio",
        });

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task Signing_in_with_the_right_password_starts_a_session()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        await member.SignOutAsync();

        var response = await member.SignInAsync(member.Email, ConsoleClient.Password);

        Assert.Equal(HttpStatusCode.OK, response.Status);
        Assert.Equal(HttpStatusCode.OK, (await member.SendAsync("getSession", HttpMethod.Get, Paths.Session)).Status);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_response()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        using var stranger = ConsoleClient.Anonymous(api);

        var wrongPassword = await stranger.SignInAsync(member.Email, "not the right password");
        var unknownEmail = await stranger.SignInAsync($"nobody-{Guid.NewGuid():N}@toggleforge.test", "not the right password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.Status);
        Assert.Equal(wrongPassword.Status, unknownEmail.Status);
        Assert.Equal(wrongPassword.Body, unknownEmail.Body);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_for_the_right_password()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        using var attacker = ConsoleClient.Anonymous(api);
        for (var attempt = 1; attempt < MaxFailedAttempts; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await attacker.SignInAsync(member.Email, "guess " + attempt)).Status);
        }

        var lockingAttempt = await attacker.SignInAsync(member.Email, "last guess");
        var rightPassword = await attacker.SignInAsync(member.Email, ConsoleClient.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, lockingAttempt.Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, rightPassword.Status);
        Assert.InRange(int.Parse(rightPassword.Header("Retry-After")!, System.Globalization.CultureInfo.InvariantCulture), 1, 900);
    }

    [Fact]
    public async Task The_old_session_cookie_is_rejected_after_signing_out()
    {
        using var member = await ConsoleClient.SignUpAsync(api);
        var oldCookie = member.SessionCookieValue();

        Assert.Equal(HttpStatusCode.NoContent, (await member.SignOutAsync()).Status);

        using var thief = api.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, Paths.Session);
        request.Headers.Add("Cookie", $"{ConsoleClient.SessionCookie}={oldCookie}");
        var response = await thief.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_is_host_only_secure_http_only_and_strict()
    {
        using var client = ConsoleClient.Anonymous(api);

        var response = await client.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new
        {
            email = $"member-{Guid.NewGuid():N}@toggleforge.test",
            password = ConsoleClient.Password,
            organizationName = "Studio",
        });

        var cookie = response.Message.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(ConsoleClient.SessionCookie + "=", StringComparison.Ordinal));
        Assert.Contains("path=/", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("domain=", cookie, StringComparison.Ordinal);
    }
}
