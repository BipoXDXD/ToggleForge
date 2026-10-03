using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace ToggleForge.Domain.Delivery;

/// <summary>
/// Segredo de uma credencial do SDK: <c>tfsdk_</c> seguido de 256 bits aleatórios em base64url.
/// <see cref="ToString"/> mostra só o prefixo, para o segredo não vazar em log.
/// </summary>
public sealed class CredentialSecret
{
    public const string Marker = "tfsdk_";

    private const int RandomBytes = 32;
    private const int EncodedLength = 43;
    private const int VisibleCharacters = 4;

    private CredentialSecret(string value) => Value = value;

    public string Value { get; }

    public string DisplayPrefix => Value[..(Marker.Length + VisibleCharacters)];

    public static CredentialSecret Generate() =>
        new(Marker + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RandomBytes)));

    public static bool TryParse(string? value, [NotNullWhen(true)] out CredentialSecret? secret)
    {
        secret = IsWellFormed(value) ? new CredentialSecret(value) : null;
        return secret is not null;
    }

    /// <summary>SHA-256 em hexadecimal. Com 256 bits de entropia, um hash lento não acrescenta proteção.</summary>
    public string ComputeHash() => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(Value)));

    public override string ToString() => DisplayPrefix + "…";

    private static bool IsWellFormed([NotNullWhen(true)] string? value) =>
        value is not null
        && value.Length == Marker.Length + EncodedLength
        && value.StartsWith(Marker, StringComparison.Ordinal)
        && Base64Url.IsValid(value.AsSpan(Marker.Length), out var decodedLength)
        && decodedLength == RandomBytes;
}
