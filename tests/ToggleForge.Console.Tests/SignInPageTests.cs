using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Pages;

namespace ToggleForge.Console.Tests;

public sealed class SignInPageTests
{
    private const string SessionJson = """
        {"member":{"id":"9b2f0c1e-5d6a-4b7c-8e9f-1a2b3c4d5e6f","email":"owner@toggleforge.test"},
         "organizations":[{"organizationId":"3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f","name":"Studio","role":"owner"}]}
        """;

    [Fact]
    public void Signing_in_returns_the_member_to_the_page_asked_for()
    {
        var api = new StubApi().Answer(HttpMethod.Post, "/api/v1/session", HttpStatusCode.OK, SessionJson);
        using var console = ConsoleRendering.Create(api);
        var navigation = console.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("sign-in?returnUrl=organizations%2F42%2Fprojects");
        var page = console.Render<SignIn>();

        SubmitCredentials(page);

        page.WaitForAssertion(() => Assert.Equal("http://localhost/organizations/42/projects", navigation.Uri));
        Assert.Equal("owner@toggleforge.test", api.Session.Current?.Member.Email);
    }

    [Fact]
    public void A_return_address_on_another_site_is_ignored()
    {
        var api = new StubApi().Answer(HttpMethod.Post, "/api/v1/session", HttpStatusCode.OK, SessionJson);
        using var console = ConsoleRendering.Create(api);
        var navigation = console.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("sign-in?returnUrl=https%3A%2F%2Fevil.example%2Fphish");
        var page = console.Render<SignIn>();

        SubmitCredentials(page);

        page.WaitForAssertion(() => Assert.Equal("http://localhost/", navigation.Uri));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "The email or the password is wrong.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many attempts. Wait a few minutes and try again.")]
    public void A_refused_sign_in_explains_what_to_do(HttpStatusCode status, string message)
    {
        var api = new StubApi().Answer(
            HttpMethod.Post, "/api/v1/session", status, $$"""{"type":"about:blank","title":"Refused","status":{{(int)status}}}""", "application/problem+json");
        using var console = ConsoleRendering.Create(api);
        var page = console.Render<SignIn>();

        SubmitCredentials(page);

        page.WaitForAssertion(() => Assert.Equal(message, page.Find("[role=alert]").TextContent));
        Assert.Null(api.Session.Current);
    }

    private static void SubmitCredentials(IRenderedComponent<SignIn> page)
    {
        page.Find("input[type=email]").Change("owner@toggleforge.test");
        page.Find("input[type=password]").Change("correct horse battery staple");
        page.Find("form").Submit();
    }
}
