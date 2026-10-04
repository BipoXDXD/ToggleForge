namespace ToggleForge.Sdk.Tests;

public sealed class SdkCredentialTests
{
    [Fact]
    public void A_credential_from_the_console_is_accepted() =>
        Assert.True(SdkCredential.TryParse("tfsdk_" + new string('a', 40) + "-_9", out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tfsdk_")]
    [InlineData("tfsdk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tfsdk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("tfsdk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa+")]
    [InlineData("TFSDK_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("Bearer tfsdk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Anything_else_is_refused(string? value) =>
        Assert.False(SdkCredential.TryParse(value, out _));

    [Fact]
    public void The_secret_never_shows_up_as_text()
    {
        var credential = SdkCredential.Parse(Snapshots.Credential);

        Assert.DoesNotContain("CANARY", credential.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CANARY", $"{credential}", StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_credential_is_not_repeated_in_the_error()
    {
        var error = Assert.Throws<ArgumentException>(() => SdkCredential.Parse("tfsdk_CANARY-too-short"));

        Assert.DoesNotContain("CANARY", error.Message, StringComparison.Ordinal);
    }
}
