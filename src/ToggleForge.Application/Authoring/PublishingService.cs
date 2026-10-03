using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public sealed class PublishingService(
    MemberAccess access,
    IMembershipDirectory memberships,
    IEnvironmentRepository environments,
    IPublicationRepository publications,
    IIdempotencyStore idempotency,
    IAuthoringUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<PublicationView>> PublishAsync(
        Guid memberId,
        EnvironmentLocator locator,
        PublishCommand command,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

        var attempt = new Attempt(memberId, idempotencyKey, Fingerprint(locator, command));
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            if (await environments.FindForUpdateAsync(locator, cancellationToken) is not { } environment)
            {
                return AccessFailures.NotFound;
            }

            if (MemberAccess.Require(role, RolePermissions.CanAuthor) is { } forbidden)
            {
                return forbidden;
            }

            if (await PublishLockedAsync(environment, command, attempt, cancellationToken) is { } published)
            {
                await transaction.CommitAsync(cancellationToken);
                return published;
            }
        }

        // Só a mesma chave usada ao mesmo tempo em outro ambiente chega aqui, porque o lock é por ambiente. A
        // transação abortou, e o registro gravado pela outra requisição decide a resposta.
        unitOfWork.DiscardChanges();
        return await ReplayAsync(attempt, cancellationToken)
            ?? throw new InvalidOperationException("The publication failed to save without a competing idempotency record.");
    }

    /// <summary>
    /// Com o ambiente já travado, então uma repetição simultânea espera a original e vira replay. Nulo quando a
    /// gravação perde a corrida pela chave de idempotência.
    /// </summary>
    private async Task<Result<PublicationView>?> PublishLockedAsync(
        ProjectEnvironment environment, PublishCommand command, Attempt attempt, CancellationToken cancellationToken)
    {
        if (await ReplayAsync(attempt, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        var current = await publications.FindAsync(environment, cancellationToken);
        var now = clock.GetUtcNowInMicroseconds();
        if (!environment.Publish(command.ReviewedDraftRevision, command.Summary, attempt.MemberId, now, current)
                .TryGetValue(out var publication, out var failure))
        {
            return failure;
        }

        publications.Add(publication);
        idempotency.Add(new IdempotencyRecord(attempt.MemberId, attempt.Key.Value, attempt.Fingerprint, environment.Id, publication.Version, now));
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return null;
        }

        return await ViewAsync(publication, cancellationToken);
    }

    /// <summary>Nulo quando a chave ainda não foi usada.</summary>
    private async Task<Result<PublicationView>?> ReplayAsync(Attempt attempt, CancellationToken cancellationToken)
    {
        if (await idempotency.FindAsync(attempt.MemberId, attempt.Key, cancellationToken) is not { } record)
        {
            return null;
        }

        if (record.Fingerprint != attempt.Fingerprint)
        {
            return PublishingFailures.IdempotencyKeyReused;
        }

        var publication = await publications.FindAsync(record.EnvironmentId, record.Version, cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency record points to missing version {record.Version}.");
        return await ViewAsync(publication, cancellationToken);
    }

    private async Task<PublicationView> ViewAsync(Publication publication, CancellationToken cancellationToken)
    {
        var emails = await memberships.FindEmailsAsync([publication.PublishedBy], cancellationToken);
        var publisherEmail = emails.TryGetValue(publication.PublishedBy, out var email)
            ? email
            : throw new InvalidOperationException($"Publisher {publication.PublishedBy} has no account.");
        return new PublicationView(
            publication.Version,
            publication.PublishedAt,
            new MemberView(publication.PublishedBy, publisherEmail),
            publication.Origin,
            publication.RestoredVersion,
            publication.Summary,
            publication.Flags);
    }

    private static string Fingerprint(EnvironmentLocator locator, PublishCommand command)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            locator.OrganizationId,
            ProjectKey = locator.ProjectKey.Value,
            EnvironmentKey = locator.EnvironmentKey.Value,
            command.ReviewedDraftRevision,
            Summary = command.Summary?.Value,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>O que a idempotência compara: quem publica, com qual chave e com qual pedido.</summary>
    private sealed record Attempt(Guid MemberId, IdempotencyKey Key, string Fingerprint);
}
