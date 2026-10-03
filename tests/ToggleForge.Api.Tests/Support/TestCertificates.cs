using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ToggleForge.Api.Tests.Support;

/// <summary>
/// Certificados autoassinados que protegem o key ring nos testes, gravados em PEM como um secret montado.
/// O host padrão usa <see cref="Current"/> e aceita <see cref="Next"/> como anterior, então as chaves que um
/// teste de rotação criar com <see cref="Next"/> continuam legíveis para os outros testes.
/// </summary>
public static class TestCertificates
{
    private static readonly string _directory = Directory.CreateTempSubdirectory("toggleforge-keyring-").FullName;

    public static PemPair Current { get; } = Create("current");

    public static PemPair Next { get; } = Create("next");

    private static PemPair Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=ToggleForge test key ring {name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pair = new PemPair(Path.Combine(_directory, $"{name}.crt"), Path.Combine(_directory, $"{name}.key"));
        File.WriteAllText(pair.CertificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(pair.KeyPath, rsa.ExportPkcs8PrivateKeyPem());
        return pair;
    }

    public sealed record PemPair(string CertificatePath, string KeyPath);
}
