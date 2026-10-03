using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// STRIDE S3: senha que aparece em vazamentos é recusada no cadastro (NIST 800-63B). STRIDE I6: o Pwned
/// Passwords só recebe os 5 primeiros hex do SHA-1, nunca a senha, o hash inteiro ou o e-mail (ADR 0007).
/// </summary>
public sealed class BreachedPasswordTests(ApiFactory api)
{
    [Fact]
    public async Task A_password_found_in_breaches_is_rejected_on_password_and_creates_nothing()
    {
        var email = NewEmail();
        using var visitor = ConsoleClient.Anonymous(api);

        var rejected = await CreateAccountAsync(visitor, email, FakePwnedPasswords.BreachedPassword);
        var retried = await CreateAccountAsync(visitor, email, ConsoleClient.Password);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.Status);
        Assert.Equal(["/password"], rejected.ErrorSources());
        Assert.Equal(HttpStatusCode.Created, retried.Status);
    }

    [Fact]
    public async Task A_suffix_that_only_comes_as_padding_is_not_a_breach()
    {
        using var visitor = ConsoleClient.Anonymous(api);

        var created = await CreateAccountAsync(visitor, NewEmail(), FakePwnedPasswords.PaddingOnlyPassword);

        Assert.Equal(HttpStatusCode.Created, created.Status);
    }

    [Fact]
    public async Task Only_the_first_5_hex_of_the_SHA1_leave_the_api_with_padding()
    {
        var email = NewEmail();
        var password = $"CANARY-{Guid.NewGuid()}";
        var hash = FakePwnedPasswords.Sha1Hex(password);
        using var visitor = ConsoleClient.Anonymous(api);

        await CreateAccountAsync(visitor, email, password);

        var sent = Assert.Single(api.PwnedPasswords.Requests, request => request.Uri.AbsolutePath.EndsWith(hash[..5], StringComparison.Ordinal));
        Assert.Equal($"https://api.pwnedpasswords.com/range/{hash[..5]}", sent.Uri.ToString());
        Assert.Contains("Add-Padding: true", sent.Headers, StringComparison.Ordinal);
        var everything = sent.Uri + sent.Headers;
        Assert.DoesNotContain(password, everything, StringComparison.Ordinal);
        Assert.DoesNotContain(hash[5..], everything, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, everything, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Fail-open (ADR 0007): o cadastro não depende do serviço externo, e a falha fica no log.</summary>
    [Fact]
    public async Task When_the_breach_service_fails_the_password_is_accepted_and_logged()
    {
        await using var host = WithPwnedPasswords(new FakePwnedPasswords(HttpStatusCode.ServiceUnavailable));
        using var visitor = ConsoleClient.Anonymous(host);
        var password = $"CANARY-{Guid.NewGuid()}";
        var warningsBefore = UnavailableWarnings();

        var created = await CreateAccountAsync(visitor, NewEmail(), password);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(warningsBefore + 1, UnavailableWarnings());
        Assert.DoesNotContain(api.Logs.Entries, entry => entry.Contains(password, StringComparison.Ordinal));
    }

    /// <summary>O cadastro espera no máximo 2 segundos: o serviço que levaria 5 é abandonado.</summary>
    [Fact]
    public async Task When_the_breach_service_hangs_the_api_gives_up_and_accepts_the_password()
    {
        var hanging = new FakePwnedPasswords(delay: TimeSpan.FromSeconds(5));
        await using var host = WithPwnedPasswords(hanging);
        using var visitor = ConsoleClient.Anonymous(host);
        var warningsBefore = UnavailableWarnings();

        var created = await CreateAccountAsync(visitor, NewEmail(), $"CANARY-{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.True(hanging.AbandonedRequests > 0);
        Assert.Equal(warningsBefore + 1, UnavailableWarnings());
    }

    private WebApplicationFactory<Program> WithPwnedPasswords(FakePwnedPasswords service) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(service.CreateHandler))));

    /// <summary>Os testes desta classe rodam em sequência, e nenhum outro teste derruba o serviço.</summary>
    private int UnavailableWarnings() =>
        api.Logs.Entries.Count(entry => entry.Contains("Pwned Passwords unavailable", StringComparison.Ordinal));

    private static Task<ApiResponse> CreateAccountAsync(ConsoleClient visitor, string email, string password) =>
        visitor.SendAsync("createAccount", HttpMethod.Post, Paths.Accounts, new { email, password, organizationName = "Breach Studio" });

    private static string NewEmail() => $"member-{Guid.NewGuid():N}@toggleforge.test";
}
