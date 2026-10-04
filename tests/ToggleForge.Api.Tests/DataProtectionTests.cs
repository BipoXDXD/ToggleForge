using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ToggleForge.Api.Tests.Support;

namespace ToggleForge.Api.Tests;

/// <summary>
/// O key ring do Data Protection fica no Postgres, cifrado pelo certificado (ADR 0008): toda instância lê os
/// cookies das outras, e quem lê o banco não forja sessão.
/// </summary>
public sealed class DataProtectionTests(ApiFactory api)
{
    [Fact]
    public async Task Every_key_in_the_database_is_encrypted_with_the_certificate()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);

        var keys = await api.DataProtectionKeyXmlAsync();

        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            Assert.Contains("<encryptedSecret", key, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);
        });
    }

    /// <summary>Rotação: com o certificado novo como atual e o antigo como anterior, as sessões abertas continuam valendo.</summary>
    [Fact]
    public async Task During_rotation_the_previous_certificate_still_opens_existing_sessions()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var rotated = WithCertificates(TestCertificates.Next, previous: TestCertificates.Current);

        var session = await SessionOnAsync(rotated, owner);

        Assert.Equal(HttpStatusCode.OK, session.Status);
    }

    [Fact]
    public async Task Without_the_certificate_that_protected_the_key_the_session_is_rejected()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await using var stranger = WithCertificates(TestCertificates.Next, previous: null);

        var session = await SessionOnAsync(stranger, owner);

        Assert.Equal(HttpStatusCode.Unauthorized, session.Status);
    }

    /// <summary>Sem um nome de aplicação fixo, o discriminador viria do caminho do app, e uma imagem nova derrubaria as sessões.</summary>
    [Fact]
    public async Task A_session_survives_an_instance_installed_in_another_folder()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        var elsewhere = Directory.CreateTempSubdirectory("toggleforge-content-root-").FullName;
        // Uma instância instalada leva o próprio appsettings.json, onde está a configuração obrigatória (HSTS).
        File.Copy(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), Path.Combine(elsewhere, "appsettings.json"));
        await using var moved = api.WithWebHostBuilder(builder => builder.UseContentRoot(elsewhere));

        var session = await SessionOnAsync(moved, owner);

        Assert.Equal(HttpStatusCode.OK, session.Status);
    }

    [Fact]
    public async Task Outside_development_the_api_does_not_start_without_the_certificate()
    {
        await using var production = api.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("DataProtection:CertificatePath", string.Empty);
            builder.UseSetting("DataProtection:KeyPath", string.Empty);
        });

        var error = Assert.Throws<InvalidOperationException>(() => production.CreateClient());

        Assert.Contains("DataProtection:CertificatePath", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DataProtection:KeyPath")]
    [InlineData("DataProtection:PreviousCertificatePath")]
    public async Task Half_a_certificate_pair_keeps_the_api_from_starting(string missingSetting)
    {
        await using var misconfigured = api.WithWebHostBuilder(builder => builder.UseSetting(missingSetting, string.Empty));

        var error = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("or neither", error.Message, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> WithCertificates(TestCertificates.PemPair current, TestCertificates.PemPair? previous) =>
        api.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DataProtection:CertificatePath", current.CertificatePath);
            builder.UseSetting("DataProtection:KeyPath", current.KeyPath);
            builder.UseSetting("DataProtection:PreviousCertificatePath", previous?.CertificatePath ?? string.Empty);
            builder.UseSetting("DataProtection:PreviousKeyPath", previous?.KeyPath ?? string.Empty);
        });

    /// <summary>O cookie de sessão emitido por uma instância, apresentado a outra.</summary>
    private static async Task<ApiResponse> SessionOnAsync(WebApplicationFactory<Program> instance, ConsoleClient owner)
    {
        using var visitor = ConsoleClient.Anonymous(instance);
        visitor.Cookies.Add(owner.Cookies.GetAllCookies());
        return await visitor.SendAsync("getSession", HttpMethod.Get, Paths.Session);
    }
}
