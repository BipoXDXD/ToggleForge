namespace ToggleForge.Domain.Delivery;

/// <summary>Credencial embutida no build do jogo. Só lê o snapshot publicado do próprio ambiente.</summary>
public sealed class Credential
{
    public const int MaxActivePerEnvironment = 5;

    /// <summary>
    /// Precisão do último uso: o polling do SDK grava no máximo um uso por credencial a cada janela, em vez de
    /// uma escrita por download.
    /// </summary>
    public static readonly TimeSpan UsePrecision = TimeSpan.FromMinutes(5);

    private Credential(
        Guid id,
        Guid environmentId,
        DisplayName name,
        string prefix,
        string secretHash,
        DateTimeOffset createdAt,
        DateTimeOffset? revokedAt)
    {
        Id = id;
        EnvironmentId = environmentId;
        Name = name;
        Prefix = prefix;
        SecretHash = secretHash;
        CreatedAt = createdAt;
        RevokedAt = revokedAt;
    }

    public Guid Id { get; }

    public Guid EnvironmentId { get; }

    public DisplayName Name { get; }

    public string Prefix { get; }

    public string SecretHash { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Último download do snapshot gravado, com a precisão de <see cref="UsePrecision"/>; nulo se nunca usada.</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    /// <summary>Um uso em <paramref name="now"/> só é gravado se o último gravado já saiu da janela de precisão.</summary>
    public bool IsUseWorthRecording(DateTimeOffset now) => LastUsedAt is null || LastUsedAt < now - UsePrecision;

    /// <summary>
    /// O segredo só existe neste retorno; a credencial guarda apenas o hash.
    /// <paramref name="activeCredentials"/> é a contagem atual do ambiente, lida sob lock.
    /// </summary>
    public static Result<IssuedCredential> Issue(Guid environmentId, DisplayName name, DateTimeOffset now, int activeCredentials)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (activeCredentials >= MaxActivePerEnvironment)
        {
            return DeliveryFailures.CredentialLimitReached;
        }

        var secret = CredentialSecret.Generate();
        var credential = new Credential(
            Guid.NewGuid(), environmentId, name, secret.DisplayPrefix, secret.ComputeHash(), now, revokedAt: null);
        return new IssuedCredential(credential, secret);
    }

    /// <summary>
    /// A partir de agora o jogo recebe 401 com esta credencial. Revogar de novo mantém o instante da primeira
    /// revogação, então repetir o pedido devolve a mesma credencial.
    /// </summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
