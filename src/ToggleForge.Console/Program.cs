using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ToggleForge.Console;
using ToggleForge.Console.Api;
using ToggleForge.Console.Session;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<SessionState>();
// A API está na mesma origem (ADR 0009): o endereço base do console é o da API.
builder.Services.AddHttpClient<ConsoleApi>(http => http.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
