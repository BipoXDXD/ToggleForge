using ToggleForge.Api;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ToggleForge");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings:ToggleForge through User Secrets or the ConnectionStrings__ToggleForge environment variable.");
}

builder.Services.AddToggleForgeApi(connectionString);
builder.AddToggleForgeDataProtection();
var app = builder.Build();

if (await MaintenanceCommands.TryRunAsync(app, args))
{
    return;
}

app.UseToggleForgeApi();
await app.RunAsync();
