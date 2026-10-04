using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Tests;

/// <summary>Um bUnit com o console ligado ao <see cref="StubApi"/>, sem navegador nem API real.</summary>
internal static class ConsoleRendering
{
    public static readonly Guid OrganizationId = Guid.Parse("3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f");

    public static BunitContext Create(StubApi api)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(api.Session);
        context.Services.AddSingleton(api.CreateClient());
        return context;
    }

    public static void SignInAs(StubApi api, string role) =>
        api.Session.Start(new Api.Session(
            new Member(Guid.Parse("9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f"), "member@toggleforge.test"),
            [new Membership(OrganizationId, "Studio", role)]));
}
