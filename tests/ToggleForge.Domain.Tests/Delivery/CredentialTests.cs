using ToggleForge.Domain.Delivery;

namespace ToggleForge.Domain.Tests.Delivery;

public sealed class CredentialTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issued_credential_keeps_only_the_hash_and_prefix_of_its_secret()
    {
        var environmentId = Guid.NewGuid();

        var issued = Credential.Issue(environmentId, Names.Of("Build 1"), _now, activeCredentials: 4);

        Assert.True(issued.TryGetValue(out var value, out _));
        var (credential, secret) = value;

        Assert.Equal(environmentId, credential.EnvironmentId);
        Assert.Equal(secret.ComputeHash(), credential.SecretHash);
        Assert.Equal(secret.DisplayPrefix, credential.Prefix);
        Assert.Equal(_now, credential.CreatedAt);
        Assert.Null(credential.RevokedAt);
    }

    [Fact]
    public void Environment_cannot_have_a_sixth_active_credential()
    {
        var issued = Credential.Issue(Guid.NewGuid(), Names.Of("Build 6"), _now, activeCredentials: 5);

        Assert.False(issued.TryGetValue(out _, out var failure));
        Assert.Equal(DeliveryFailures.CredentialLimitReached, failure);
    }

    [Fact]
    public void Generated_secrets_follow_the_documented_format_and_are_unique()
    {
        var first = CredentialSecret.Generate();
        var second = CredentialSecret.Generate();

        Assert.Matches("^tfsdk_[A-Za-z0-9_-]{43}$", first.Value);
        Assert.NotEqual(first.Value, second.Value);
        Assert.True(CredentialSecret.TryParse(first.Value, out var parsed));
        Assert.Equal(first.ComputeHash(), parsed.ComputeHash());
    }

    [Fact]
    public void Hash_is_the_lowercase_hex_sha256_of_the_secret()
    {
        const string Secret = "tfsdk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        Assert.True(CredentialSecret.TryParse(Secret, out var secret));

        // printf %s "$Secret" | shasum -a 256
        Assert.Equal("dbd6f6429655730d4880b66dab17f49a77ccdbf251e1ce1c71a28eb2d40a68d3", secret.ComputeHash());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tfsdk_")]
    [InlineData("TFSDK_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("tfsdk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("tfsdk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("tfsdk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void TryParse_rejects_anything_but_the_documented_format(string? value)
    {
        Assert.False(CredentialSecret.TryParse(value, out _));
    }

    [Fact]
    public void ToString_never_reveals_the_secret()
    {
        var secret = CredentialSecret.Generate();

        Assert.DoesNotContain(secret.Value, secret.ToString(), StringComparison.Ordinal);
        Assert.StartsWith(secret.DisplayPrefix, secret.ToString(), StringComparison.Ordinal);
    }
}
