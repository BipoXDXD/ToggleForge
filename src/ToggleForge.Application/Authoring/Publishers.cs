using ToggleForge.Application.Access;
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

/// <summary>Quem publicou cada versão, pelo e-mail que o módulo Access guarda.</summary>
internal static class Publishers
{
    public static async Task<PublicationView> ViewAsync(
        this IMembershipDirectory memberships, Publication publication, CancellationToken cancellationToken)
    {
        var publishers = await memberships.FindPublishersAsync([publication.PublishedBy], cancellationToken);
        return new PublicationView(
            publication.Version,
            publication.PublishedAt,
            publishers[publication.PublishedBy],
            publication.Origin,
            publication.RestoredVersion,
            publication.Summary,
            publication.Flags);
    }

    /// <summary>Lança se um autor não tiver conta: contas ainda não são apagadas, então seria dado corrompido.</summary>
    public static async Task<IReadOnlyDictionary<Guid, MemberView>> FindPublishersAsync(
        this IMembershipDirectory memberships, IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken)
    {
        var emails = await memberships.FindEmailsAsync(memberIds, cancellationToken);
        return memberIds.Distinct().ToDictionary(
            id => id,
            id => new MemberView(id, emails.TryGetValue(id, out var email)
                ? email
                : throw new InvalidOperationException($"Publisher {id} has no account.")));
    }
}
