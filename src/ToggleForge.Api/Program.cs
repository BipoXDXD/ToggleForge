using ToggleForge.Api;
using ToggleForge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ToggleForge");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings:ToggleForge through User Secrets or the ConnectionStrings__ToggleForge environment variable.");
}

builder.Services.AddToggleForgeApi(connectionString);
var app = builder.Build();

// Migrations rodam num passo explícito do deploy, nunca a cada boot de instância.
if (args.Contains("--migrate"))
{
    await app.Services.MigrateToggleForgeDatabaseAsync(CancellationToken.None);
    return;
}

app.UseToggleForgeApi();
await app.RunAsync();
