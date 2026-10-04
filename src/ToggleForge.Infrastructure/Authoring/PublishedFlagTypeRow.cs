using ToggleForge.Domain.Authoring;

namespace ToggleForge.Infrastructure.Authoring;

/// <summary>
/// O tipo com que a chave foi publicada no ambiente pela primeira vez. Toda linha de <c>publication_flags</c> aponta
/// para cá pela chave estrangeira (ambiente, chave, tipo), então o banco recusa publicar a chave com outro tipo.
/// A linha nunca é apagada nem alterada: apagar a flag do rascunho não libera o tipo.
/// </summary>
internal sealed class PublishedFlagTypeRow
{
    public required Guid EnvironmentId { get; init; }

    public required string Key { get; init; }

    public required FlagType Type { get; init; }
}
