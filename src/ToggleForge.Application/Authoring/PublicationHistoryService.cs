using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;

namespace ToggleForge.Application.Authoring;

/// <summary>Leitura do histórico de publicações, aberta a qualquer membro da organização.</summary>
public sealed class PublicationHistoryService(
    MemberAccess access,
    IEnvironmentDirectory environments,
    IPublicationRepository publications,
    IMembershipDirectory memberships)
{
    /// <summary>Da mais nova para a mais antiga, começando abaixo de <see cref="PageRequest.BeforeVersion"/>.</summary>
    public async Task<Result<PublicationPage>> ListAsync(
        Guid memberId, EnvironmentLocator locator, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!(await FindEnvironmentAsync(memberId, locator, cancellationToken)).TryGetValue(out var environmentId, out var failure))
        {
            return failure;
        }

        // Um item a mais diz se existe próxima página sem contar a coleção inteira.
        var headers = await publications.ListAsync(environmentId, page.BeforeVersion, page.Size + 1, cancellationToken);
        var items = headers.Take(page.Size).ToList();
        var publishers = await memberships.FindPublishersAsync(items.Select(header => header.PublishedBy).ToList(), cancellationToken);
        return new PublicationPage(
            items.Select(header => PublicationSummaryView.Of(header, publishers[header.PublishedBy])).ToList(),
            headers.Count > page.Size ? items[^1].Version : null);
    }

    public async Task<Result<PublicationView>> GetAsync(
        Guid memberId, EnvironmentLocator locator, long version, CancellationToken cancellationToken)
    {
        if (!(await FindEnvironmentAsync(memberId, locator, cancellationToken)).TryGetValue(out var environmentId, out var failure))
        {
            return failure;
        }

        return await publications.FindAsync(environmentId, version, cancellationToken) is { } publication
            ? await memberships.ViewAsync(publication, cancellationToken)
            : PublishingFailures.PublicationNotFound;
    }

    private async Task<Result<Guid>> FindEnvironmentAsync(Guid memberId, EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out _, out var failure))
        {
            return failure;
        }

        return await environments.FindIdAsync(locator, cancellationToken) is { } id ? id : AccessFailures.NotFound;
    }
}
