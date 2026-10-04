using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// STRIDE I4 e D2: cada tipo de cliente tem um teto por minuto e recebe 429 com Retry-After ao passar dele.
/// Cada teste sobe a API com um teto baixo e endereços de cliente simulados.
/// </summary>
public sealed class RateLimitTests(ApiFactory api)
{
    private const string Guesser = "203.0.113.7";
    private const string Neighbor = "203.0.113.8";

    [Fact]
    public async Task Guessing_many_accounts_from_one_address_gets_429_with_Retry_After()
    {
        await using var host = WithLimit("AnonymousPerMinute", 5);
        using var guesser = ConsoleClient.Anonymous(host);
        guesser.ComeFrom(Guesser);

        // O token antiforgery conta como a primeira das cinco requisições.
        var attempts = new List<ApiResponse>();
        for (var nth = 1; nth <= 5; nth++)
        {
            attempts.Add(await guesser.SendAsync(
                "signIn", HttpMethod.Post, Paths.Session, new { email = $"nobody-{nth}@toggleforge.test", password = "wrong password" }));
        }

        Assert.All(attempts[..4], attempt => Assert.Equal(HttpStatusCode.Unauthorized, attempt.Status));
        AssertTooManyRequests(attempts[4]);
    }

    [Fact]
    public async Task Probing_emails_through_sign_up_from_one_address_gets_429()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var host = WithLimit("AnonymousPerMinute", 4);
        using var prober = ConsoleClient.Anonymous(host);
        prober.ComeFrom(Guesser);

        var attempts = new List<ApiResponse>();
        for (var nth = 1; nth <= 4; nth++)
        {
            attempts.Add(await prober.SendAsync(
                "createAccount", HttpMethod.Post, Paths.Accounts, new { email = owner.Email, password = ConsoleClient.Password, organizationName = "Probe" }));
        }

        Assert.All(attempts[..3], attempt => Assert.Equal(HttpStatusCode.Conflict, attempt.Status));
        AssertTooManyRequests(attempts[3]);
    }

    [Fact]
    public async Task Each_address_has_its_own_anonymous_limit()
    {
        await using var host = WithLimit("AnonymousPerMinute", 1);
        using var guesser = ConsoleClient.Anonymous(host);
        using var neighbor = ConsoleClient.Anonymous(host);
        guesser.ComeFrom(Guesser);
        neighbor.ComeFrom(Neighbor);
        var clock = Stopwatch.StartNew();
        var first = await GetAntiforgeryTokenAsync(guesser);

        var blocked = await GetAntiforgeryTokenAsync(guesser);
        var allowed = await GetAntiforgeryTokenAsync(neighbor);

        // Falhou uma vez sob carga sem dizer qual chamada. A mensagem traz as três respostas e o tempo, porque a
        // janela de 1 minuto só renova se o guesser levar isso entre as duas chamadas.
        var calls = string.Create(
            CultureInfo.InvariantCulture,
            $"first {Describe(first)}; blocked {Describe(blocked)}; allowed {Describe(allowed)}; {clock.ElapsedMilliseconds} ms in all");
        Assert.True(blocked.Status == HttpStatusCode.TooManyRequests && allowed.Status == HttpStatusCode.OK, calls);
        AssertTooManyRequests(blocked);
    }

    /// <summary>Um cliente IPv6 costuma ter um /64 inteiro: trocar de endereço dentro dele não renova o limite.</summary>
    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2:ffff::9", HttpStatusCode.TooManyRequests)]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:3::1", HttpStatusCode.OK)]
    [InlineData("203.0.113.7", "::ffff:203.0.113.7", HttpStatusCode.TooManyRequests)]
    [InlineData("203.0.113.7", "203.0.113.8", HttpStatusCode.OK)]
    public async Task The_anonymous_limit_counts_IPv6_by_64_bit_network(string first, string second, HttpStatusCode expected)
    {
        await using var host = WithLimit("AnonymousPerMinute", 1);
        using var client = ConsoleClient.Anonymous(host);
        client.ComeFrom(first);
        await GetAntiforgeryTokenAsync(client);

        client.ComeFrom(second);
        var response = await GetAntiforgeryTokenAsync(client);

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task A_member_over_the_limit_gets_429_even_from_another_address()
    {
        await using var host = WithLimit("MemberPerMinute", 2);
        using var owner = await ConsoleClient.SignUpAsync(host);
        owner.ComeFrom(Guesser);
        await owner.ListProjectsAsync();
        await owner.ListProjectsAsync();

        owner.ComeFrom(Neighbor);
        var blocked = await owner.ListProjectsAsync();

        AssertTooManyRequests(blocked);
    }

    [Fact]
    public async Task One_member_over_the_limit_does_not_block_the_rest_of_the_organization()
    {
        await using var host = WithLimit("MemberPerMinute", 2);
        using var owner = await ConsoleClient.SignUpAsync(host);
        using var colleague = await ConsoleClient.SignUpAsync(host);
        await api.AddMembershipAsync(owner.OrganizationId, colleague.MemberId, "viewer");
        for (var nth = 1; nth <= 3; nth++)
        {
            await owner.ListProjectsAsync();
        }

        var allowed = await colleague.ListProjectsAsync(owner.OrganizationId);

        Assert.Equal(HttpStatusCode.OK, allowed.Status);
    }

    [Fact]
    public async Task A_game_client_over_the_limit_gets_429_with_Retry_After()
    {
        var secrets = await PublishedCredentialsAsync(count: 1);
        await using var host = WithLimit("GameClientPerMinute", 2);
        await DownloadAsync(host, secrets[0], Guesser);
        await DownloadAsync(host, secrets[0], Guesser);

        var blocked = await DownloadAsync(host, secrets[0], Guesser);

        AssertTooManyRequests(blocked);
    }

    /// <summary>Um cliente abusivo não derruba a sincronização dos outros jogadores do mesmo jogo.</summary>
    [Fact]
    public async Task Other_players_with_the_same_credential_keep_syncing()
    {
        var secrets = await PublishedCredentialsAsync(count: 1);
        await using var host = WithLimit("GameClientPerMinute", 1);
        await DownloadAsync(host, secrets[0], Guesser);

        var neighbor = await DownloadAsync(host, secrets[0], Neighbor);

        Assert.Equal(HttpStatusCode.OK, neighbor.Status);
    }

    [Fact]
    public async Task Another_credential_from_the_same_address_keeps_syncing()
    {
        var secrets = await PublishedCredentialsAsync(count: 2);
        await using var host = WithLimit("GameClientPerMinute", 1);
        await DownloadAsync(host, secrets[0], Guesser);

        var other = await DownloadAsync(host, secrets[1], Guesser);

        Assert.Equal(HttpStatusCode.OK, other.Status);
    }

    [Theory]
    [InlineData("MemberPerMinute")]
    [InlineData("GameClientPerMinute")]
    [InlineData("AnonymousPerMinute")]
    public async Task A_limit_below_1_keeps_the_api_from_starting(string limit)
    {
        await using var host = WithLimit(limit, 0);

        var error = Assert.Throws<OptionsValidationException>(() => host.CreateClient());

        Assert.Contains(limit, error.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> WithLimit(string limit, int permitsPerMinute) =>
        api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"RateLimits:{limit}", permitsPerMinute.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ClientAddresses>());
        });

    /// <summary>Segredos de credenciais de um ambiente com uma publicação, criados fora do host limitado.</summary>
    private async Task<List<string>> PublishedCredentialsAsync(int count)
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var secrets = new List<string>();
        for (var nth = 0; nth < count; nth++)
        {
            secrets.Add((await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>());
        }

        return secrets;
    }

    private static Task<ApiResponse> DownloadAsync(WebApplicationFactory<Program> host, string secret, string address) =>
        GameClient.GetSnapshotAsync(host, secret, request => request.Headers.Add(ClientAddresses.Header, address));

    private static Task<ApiResponse> GetAntiforgeryTokenAsync(ConsoleClient client) =>
        client.SendAsync("getAntiforgeryToken", HttpMethod.Get, Paths.AntiforgeryToken);

    // A resposta 200 traz o token antiforgery, então só o status entra na mensagem. As de erro (429 e 500) são
    // Problem Details sem segredo, e o corpo mostra qual foi.
    private static string Describe(ApiResponse response) =>
        response.Status == HttpStatusCode.OK
            ? "200"
            : string.Create(CultureInfo.InvariantCulture, $"{(int)response.Status} {response.Body}");

    private static void AssertTooManyRequests(ApiResponse response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.Status);
        Assert.Equal("/problems/too-many-requests", response.ProblemType);
        Assert.InRange(int.Parse(response.Header("Retry-After")!, CultureInfo.InvariantCulture), 1, 60);
    }
}
