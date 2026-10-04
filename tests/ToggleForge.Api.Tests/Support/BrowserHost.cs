using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// A API num Kestrel de verdade, em HTTPS, para um navegador carregar o console: o TestServer não abre porta, e
/// o cookie de sessão <c>__Host-</c> só existe em HTTPS. Também serve para o que só o servidor faz, como o limite
/// de tamanho do corpo, que o TestServer ignora. Usa o mesmo banco e as mesmas chaves do <see cref="ApiFactory"/>,
/// então uma conta criada por lá entra por aqui.
/// </summary>
public sealed class BrowserHost : WebApplicationFactory<Program>
{
    private readonly ApiFactory _api;
    private readonly X509Certificate2 _certificate = SelfSignedTlsCertificate();

    public BrowserHost(ApiFactory api)
    {
        _api = api;
        UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(_certificate)));
        StartServer();
    }

    public Uri Address => new(
        (Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel did not report the address it listens on.")).Addresses.Single());

    /// <summary>Um cliente HTTP sem navegador para a porta do Kestrel, que confia só no certificado deste host.</summary>
    public HttpClient CreateTlsClient(CookieContainer cookies) =>
        new(new SocketsHttpHandler
        {
            CookieContainer = cookies,
            SslOptions = { RemoteCertificateValidationCallback = (_, certificate, _, _) => _certificate.Equals(certificate) },
        })
        {
            BaseAddress = Address,
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder) => _api.ConfigureTestHost(builder);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _certificate.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>O navegador ignora erros de certificado nos testes; o certificado só precisa servir para TLS.</summary>
    private static X509Certificate2 SelfSignedTlsCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // No macOS o SslStream não aceita chave efêmera: o certificado volta por PKCS#12.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), password: null);
    }
}
