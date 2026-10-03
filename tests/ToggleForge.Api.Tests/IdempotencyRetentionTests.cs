using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O contrato guarda cada chave de idempotência por pelo menos 24 horas; o comando agendado esquece as mais
/// velhas. A API publica parada em 2001, para a limpeza só alcançar registros destes testes, e o comando
/// roda com o próprio relógio, 24 horas depois.
/// </summary>
public sealed class IdempotencyRetentionTests(ApiFactory api)
{
    private static readonly DateTimeOffset _start = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>O prazo do contrato ("Kept for 24 hours").</summary>
    private static readonly TimeSpan _retention = TimeSpan.FromHours(24);

    [Theory]
    [InlineData(0, HttpStatusCode.Conflict)]
    [InlineData(1, HttpStatusCode.Created)]
    public async Task The_purge_forgets_keys_only_after_24_hours(int microsecondsLater, HttpStatusCode expected)
    {
        var clock = new FakeTimeProvider(_start);
        await using var clocked = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton<TimeProvider>(clock)));
        using var editor = await ConsoleClient.SignUpAsync(clocked);
        await editor.CreateProjectAsync("space-race");
        var flag = await editor.CreateFlagAsync("space-race", "new-shop");
        var key = ConsoleClient.NewIdempotencyKey();
        Assert.Equal(HttpStatusCode.Created, (await editor.PublishAsync("space-race", ConsoleClient.Revision(flag), key)).Status);
        var changed = await editor.UpdateFlagAsync("space-race", "new-shop", flag.Header("ETag"), """{"enabled":false}""");

        var purgeClock = new FakeTimeProvider(_start + _retention + TimeSpan.FromMicroseconds(microsecondsLater));

        var ran = await RunMaintenanceAsync(purgeClock, "--purge-idempotency-records");
        var reused = await editor.PublishAsync("space-race", ConsoleClient.Revision(changed), key);

        Assert.True(ran);
        Assert.Equal(expected, reused.Status);
    }

    [Fact]
    public async Task Without_a_maintenance_command_the_api_goes_on_to_start()
    {
        var started = await RunMaintenanceAsync(TimeProvider.System);

        Assert.False(started);
    }

    /// <summary>O mesmo caminho do <c>Program</c>: a API montada com o banco dos testes e só o comando pedido.</summary>
    private async Task<bool> RunMaintenanceAsync(TimeProvider clock, params string[] args)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddToggleForgeApi(api.ConnectionString);
        builder.Services.AddSingleton(clock);
        await using var app = builder.Build();
        return await MaintenanceCommands.TryRunAsync(app, args);
    }
}
