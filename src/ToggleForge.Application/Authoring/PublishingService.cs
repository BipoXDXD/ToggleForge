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
    public Task<Result<PublicationView>> PublishAsync(
        Guid memberId,
        EnvironmentLocator locator,
        PublishCommand command,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        var request = new { command.ReviewedDraftRevision, Summary = command.Summary?.Value };
        var attempt = new Attempt(memberId, idempotencyKey, Fingerprint("publishDraft", locator, request));
        return RecordAsync(
            locator,
            attempt,
            (environment, current, now, _) => Task.FromResult(
                environment.Publish(command.ReviewedDraftRevision, command.Summary, memberId, now, current)),
            cancellationToken);
    }

    public Task<Result<PublicationView>> RollBackAsync(
        Guid memberId,
        EnvironmentLocator locator,
        RollbackCommand command,
        IdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        var attempt = new Attempt(memberId, idempotencyKey, Fingerprint("rollBack", locator, command));
        return RecordAsync(
            locator,
            attempt,
            async (environment, current, now, ct) => environment.RollBack(
                await publications.FindAsync(environment.Id, command.TargetVersion, ct),
                command.ExpectedCurrentVersion,
                command.DiscardDraftChanges,
                memberId,
                now,
                current),
            cancellationToken);
    }

    /// <summary>
    /// Esqueleto de toda operação que cria uma publicação: papel, ambiente travado, idempotência e gravação na
    /// mesma transação.
    /// </summary>
    private async Task<Result<PublicationView>> RecordAsync(
        EnvironmentLocator locator, Attempt attempt, PublicationChange change, CancellationToken cancellationToken)
    {
        if (!(await access.FindRoleAsync(attempt.MemberId, locator.OrganizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

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

            if (await RecordLockedAsync(environment, change, attempt, cancellationToken) is { } recorded)
            {
                await transaction.CommitAsync(cancellationToken);
                return recorded;
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
    private async Task<Result<PublicationView>?> RecordLockedAsync(
        ProjectEnvironment environment, PublicationChange change, Attempt attempt, CancellationToken cancellationToken)
    {
        if (await ReplayAsync(attempt, cancellationToken) is { } replayed)
        {
            return replayed;
        }

        var current = await publications.FindAsync(environment, cancellationToken);
        var now = clock.GetUtcNowInMicroseconds();
        if (!(await change(environment, current, now, cancellationToken)).TryGetValue(out var publication, out var failure))
        {
            return failure;
        }

        publications.Add(publication);
        idempotency.Add(new IdempotencyRecord(attempt.MemberId, attempt.Key.Value, attempt.Fingerprint, environment.Id, publication.Version, now));
        if (await unitOfWork.SaveChangesAsync(cancellationToken) != SaveOutcome.Saved)
        {
            return null;
        }

        return await memberships.ViewAsync(publication, cancellationToken);
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
        return await memberships.ViewAsync(publication, cancellationToken);
    }

    /// <summary>
    /// A operação entra no fingerprint para a mesma chave não servir a publicar e a reverter. O pedido é
    /// serializado pelo tipo concreto, então só pode ter valores primitivos.
    /// </summary>
    private static string Fingerprint(string operation, EnvironmentLocator locator, object request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Operation = operation,
            locator.OrganizationId,
            ProjectKey = locator.ProjectKey.Value,
            EnvironmentKey = locator.EnvironmentKey.Value,
            Request = request,
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>A mudança no ambiente travado que gera a publicação, dada a publicação em vigor.</summary>
    private delegate Task<Result<Publication>> PublicationChange(
        ProjectEnvironment environment, Publication? current, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>O que a idempotência compara: quem publica, com qual chave e com qual pedido.</summary>
    private sealed record Attempt(Guid MemberId, IdempotencyKey Key, string Fingerprint);
}
