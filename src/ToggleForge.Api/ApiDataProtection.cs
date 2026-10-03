using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using ToggleForge.Infrastructure;

namespace ToggleForge.Api;

/// <summary>
/// Chaves do Data Protection (cookie de sessão, antiforgery, tokens de página) no Postgres, cifradas por um
/// certificado montado como segredo (ADR 0008). Assim todas as instâncias e restarts leem os mesmos cookies.
/// </summary>
internal static class ApiDataProtection
{
    public const string Section = "DataProtection";

    /// <summary>Fixo: sem ele, o discriminador vem do caminho do app, e instâncias diferentes não se entenderiam.</summary>
    private const string ApplicationName = "ToggleForge";

    public static WebApplicationBuilder AddToggleForgeDataProtection(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = builder.Configuration.GetSection(Section).Get<KeyRingCertificates>() ?? new KeyRingCertificates();
        var dataProtection = builder.Services.AddDataProtection().SetApplicationName(ApplicationName).PersistKeysToToggleForgeDatabase();
        if (settings.LoadCurrent() is { } current)
        {
            dataProtection.ProtectKeysWithCertificate(current);
            if (settings.LoadPrevious() is { } previous)
            {
                dataProtection.UnprotectKeysWithAnyCertificate(previous);
            }
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Set {Section}:CertificatePath and {Section}:KeyPath to the PEM files that protect the key ring.");
        }

        return builder;
    }

    /// <summary>
    /// Certificados em PEM, como um secret do Kubernetes ou do Container Apps os monta. O anterior só decifra:
    /// fica configurado durante a rotação, até as chaves que ele cifrou expirarem.
    /// </summary>
    private sealed class KeyRingCertificates
    {
        public string? CertificatePath { get; set; }

        public string? KeyPath { get; set; }

        public string? PreviousCertificatePath { get; set; }

        public string? PreviousKeyPath { get; set; }

        public X509Certificate2? LoadCurrent() => Load(CertificatePath, KeyPath, nameof(CertificatePath), nameof(KeyPath));

        public X509Certificate2? LoadPrevious() =>
            Load(PreviousCertificatePath, PreviousKeyPath, nameof(PreviousCertificatePath), nameof(PreviousKeyPath));

        /// <summary>Metade de um par configurada é engano de deploy: falha na partida em vez de seguir sem cifra.</summary>
        private static X509Certificate2? Load(string? certificatePath, string? keyPath, string certificateSetting, string keySetting)
        {
            var hasKey = !string.IsNullOrWhiteSpace(keyPath);
            if (string.IsNullOrWhiteSpace(certificatePath))
            {
                return hasKey ? throw HalfPair(certificateSetting, keySetting) : null;
            }

            return hasKey ? X509Certificate2.CreateFromPemFile(certificatePath, keyPath) : throw HalfPair(certificateSetting, keySetting);
        }

        private static InvalidOperationException HalfPair(string certificateSetting, string keySetting) =>
            new($"Set both {Section}:{certificateSetting} and {Section}:{keySetting}, or neither.");
    }
}
