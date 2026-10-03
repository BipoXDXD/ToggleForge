using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace ToggleForge.Api.Http;

/// <summary>
/// Token de paginação opaco (ADP cap. 21): cifrado e autenticado pelo Data Protection, preso ao membro e à
/// coleção, e válido por <see cref="Lifetime"/>. O cursor dentro dele nunca chega ao cliente em claro, e um
/// token de outro membro, de outra coleção, adulterado ou vencido é recusado como entrada inválida.
/// </summary>
internal sealed class PageTokens(IDataProtectionProvider protection, TimeProvider clock)
{
    public const int MaxLength = 256;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    // Layout do conteúdo cifrado: membro (16 bytes), resumo da coleção (16), cursor (8) e vencimento em
    // segundos Unix (8), inteiros em big-endian.
    private const int CollectionOffset = 16;
    private const int CollectionDigestLength = 16;
    private const int CursorOffset = 32;
    private const int ExpiryOffset = 40;
    private const int PayloadLength = 48;

    private readonly IDataProtector _protector = protection.CreateProtector("ToggleForge.PageToken.v1");

    /// <param name="collection">Identifica a coleção paginada, por exemplo o path dela.</param>
    public string Issue(Guid memberId, string collection, long cursor)
    {
        var payload = new byte[PayloadLength];
        memberId.TryWriteBytes(payload.AsSpan(0, CollectionOffset));
        CollectionDigest(collection).CopyTo(payload, CollectionOffset);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(CursorOffset), cursor);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(ExpiryOffset), (clock.GetUtcNow() + Lifetime).ToUnixTimeSeconds());
        return WebEncoders.Base64UrlEncode(_protector.Protect(payload));
    }

    public bool TryRead(string token, Guid memberId, string collection, out long cursor)
    {
        cursor = 0;
        if (token.Length > MaxLength || Unprotect(token) is not { Length: PayloadLength } payload)
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(ExpiryOffset)));
        var issuedHere = new Guid(payload.AsSpan(0, CollectionOffset)) == memberId
            && payload.AsSpan(CollectionOffset, CollectionDigestLength).SequenceEqual(CollectionDigest(collection));
        if (!issuedHere || clock.GetUtcNow() >= expiresAt)
        {
            return false;
        }

        cursor = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(CursorOffset));
        return true;
    }

    private byte[]? Unprotect(string token)
    {
        try
        {
            return _protector.Unprotect(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            // Não é base64url: o cliente não mandou um token nosso.
            return null;
        }
        catch (CryptographicException)
        {
            // Adulterado, truncado ou cifrado com uma chave que não existe mais.
            return null;
        }
    }

    private static byte[] CollectionDigest(string collection) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(collection))[..CollectionDigestLength];
}
