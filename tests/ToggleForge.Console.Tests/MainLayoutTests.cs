using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ToggleForge.Console.Layout;

namespace ToggleForge.Console.Tests;

public sealed class MainLayoutTests
{
    [Fact]
    public void Without_a_session_the_member_goes_to_sign_in_and_keeps_the_page_asked_for()
    {
        var api = new StubApi().Answer(
            HttpMethod.Get, "/api/v1/session", HttpStatusCode.Unauthorized, """{"type":"about:blank","title":"Unauthorized","status":401}""", "application/problem+json");
        using var console = ConsoleRendering.Create(api);
        var navigation = console.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("organizations/42/projects");

        var layout = console.Render<MainLayout>(parameters => parameters.Add(main => main.Body, "page"));

        layout.WaitForAssertion(() => Assert.Equal("http://localhost/sign-in?returnUrl=organizations%2F42%2Fprojects", navigation.Uri));
        Assert.Empty(layout.FindAll("[role=alert]"));
    }

    [Fact]
    public void A_failure_other_than_401_is_shown_instead_of_the_page()
    {
        var api = new StubApi().Fail(HttpMethod.Get, "/api/v1/session");
        using var console = ConsoleRendering.Create(api);

        var layout = console.Render<MainLayout>(parameters => parameters.Add(main => main.Body, "page"));

        layout.WaitForAssertion(() => Assert.Equal("Check your connection and try again.", layout.Find("[role=alert]").TextContent.Trim()));
        Assert.DoesNotContain("page", layout.Markup, StringComparison.Ordinal);
    }
}
