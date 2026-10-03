using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using ToggleForge.Infrastructure;

[assembly: AssemblyFixture(typeof(ToggleForge.Api.Tests.Support.ApiFactory))]

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// A API inteira sobre um Postgres real (Khorikov: banco próprio é dependência gerenciada). Cada teste cria
/// as próprias contas e organizações, então os testes não dividem dados e não precisam limpar o banco.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Uri BaseAddress = new("https://localhost");

    private const string Unlimited = "1000000";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public LogCapture Logs { get; } = new();

    public FakePwnedPasswords PwnedPasswords { get; } = new();

    /// <summary>Para os testes que sobem o binário da API fora do TestServer, como os comandos de manutenção.</summary>
    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await Services.MigrateToggleForgeDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public HttpClient CreateAnonymousClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = BaseAddress, AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>Atalho de teste para o que ainda não tem operação na API: convite de membros fica para depois do Marco 2.</summary>
    public async Task AddMembershipAsync(Guid organizationId, Guid memberId, string role)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "insert into access.memberships (organization_id, member_id, role) values ($1, $2, $3)", connection)
        {
            Parameters = { new() { Value = organizationId }, new() { Value = memberId }, new() { Value = role } },
        };
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:ToggleForge", _postgres.GetConnectionString());
        // Todo teste vem do mesmo "IP" e cria contas: os tetos reais ficam para RateLimitTests.
        builder.UseSetting("RateLimits:MemberPerMinute", Unlimited);
        builder.UseSetting("RateLimits:GameClientPerMinute", Unlimited);
        builder.UseSetting("RateLimits:AnonymousPerMinute", Unlimited);
        builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(new HundredNanosecondClock());
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(PwnedPasswords.CreateHandler));
        });
    }
}
