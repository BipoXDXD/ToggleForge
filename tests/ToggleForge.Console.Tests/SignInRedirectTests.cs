using ToggleForge.Console.Session;

namespace ToggleForge.Console.Tests;

public sealed class SignInRedirectTests
{
    private static readonly Uri _consoleBase = new("https://console.toggleforge.test/");

    [Theory]
    [InlineData("organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects")]
    [InlineData("organizations/3f1c7d8e-2b4a-4c6d-9e8f-0a1b2c3d4e5f/projects/space-race/environments/dev")]
    public void A_path_inside_the_console_is_where_the_member_returns(string returnUrl) =>
        Assert.Equal(new Uri(_consoleBase, returnUrl), SignInRedirect.SafeReturnUri(returnUrl, _consoleBase));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    [InlineData(" //evil.example/phish")]
    [InlineData("  https://evil.example/phish")]
    [InlineData("\\\\evil.example")]
    [InlineData("https://console.toggleforge.test@evil.example/")]
    [InlineData("http://console.toggleforge.test/projects")]
    [InlineData("javascript:alert(1)")]
    public void A_destination_outside_the_console_returns_to_the_home_page(string? returnUrl) =>
        Assert.Equal(_consoleBase, SignInRedirect.SafeReturnUri(returnUrl, _consoleBase));

    [Theory]
    [InlineData("/\\evil.example")]
    [InlineData("..///evil.example")]
    [InlineData(" //evil.example/phish")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("projects\u0000")]
    public void Odd_text_still_lands_inside_the_console(string returnUrl)
    {
        var destination = SignInRedirect.SafeReturnUri(returnUrl, _consoleBase);

        Assert.Equal(_consoleBase.Authority, destination.Authority);
        Assert.Equal(_consoleBase.Scheme, destination.Scheme);
    }

    [Fact]
    public void The_sign_in_link_carries_the_page_the_member_asked_for() =>
        Assert.Equal(
            "sign-in?returnUrl=organizations%2F42%2Fprojects",
            SignInRedirect.SignInPathFor("organizations/42/projects"));

    [Theory]
    [InlineData("")]
    [InlineData("sign-in")]
    [InlineData("sign-in?returnUrl=organizations%2F42%2Fprojects")]
    public void Leaving_the_home_or_the_sign_in_page_needs_no_return(string returnPath) =>
        Assert.Equal("sign-in", SignInRedirect.SignInPathFor(returnPath));
}
