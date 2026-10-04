using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

/// <summary>
/// Avalia todas as flags do rascunho ou de uma publicação para um jogador, com o mesmo código do SDK. Só
/// lê, então qualquer membro da organização pode simular.
/// </summary>
public sealed class SimulationService(
    MemberAccess access,
    IEnvironmentRepository environments,
    IEnvironmentDirectory directory,
    IPublicationRepository publications)
{
    /// <param name="version">Versão publicada a simular; nula simula o rascunho.</param>
    public async Task<Result<SimulationView>> SimulateAsync(
        Guid memberId, EnvironmentLocator locator, long? version, PlayerId playerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(playerId);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out _, out var failure))
        {
            return failure;
        }

        var flags = version is { } published
            ? await PublishedFlagsAsync(locator, published, cancellationToken)
            : await DraftFlagsAsync(locator, cancellationToken);
        if (!flags.TryGetValue(out var simulated, out failure))
        {
            return failure;
        }

        var player = EvaluationContext.ForPlayer(playerId);
        return new SimulationView(
            version,
            simulated
                .OrderBy(flag => flag.Key.Value, StringComparer.Ordinal)
                .Select(flag => Evaluate(flag, locator, player))
                .ToList());
    }

    /// <summary>
    /// O jogador sempre tem ID e o parser da flag JSON não lança, então o valor padrão nunca é usado: só o rollout
    /// parcial sem ID ou um parser que falha cairiam nele.
    /// </summary>
    private static SimulationResultView Evaluate(PublishedFlag flag, EnvironmentLocator locator, EvaluationContext player) =>
        flag.ToFlag() switch
        {
            BooleanFlag boolean => View(flag, boolean.Evaluate(locator.ProjectKey, locator.EnvironmentKey, player, defaultValue: false), value => value ? "true" : "false"),
            JsonFlag json => View(flag, json.Evaluate(locator.ProjectKey, locator.EnvironmentKey, player, defaultValue: "null", text => text), text => text),
            var unknown => throw new InvalidOperationException($"Unknown flag '{unknown.GetType().Name}'."),
        };

    private static SimulationResultView View<T>(PublishedFlag flag, EvaluationResult<T> result, Func<T, string> json) =>
        new(flag.Key, flag.Settings.Type, json(result.Value), result.Reason, result.Bucket);

    private async Task<Result<List<PublishedFlag>>> DraftFlagsAsync(EnvironmentLocator locator, CancellationToken cancellationToken) =>
        await environments.FindAsync(locator, cancellationToken) is { } environment
            ? environment.Flags.Select(flag => flag.ToPublished()).ToList()
            : AccessFailures.NotFound;

    private async Task<Result<List<PublishedFlag>>> PublishedFlagsAsync(
        EnvironmentLocator locator, long version, CancellationToken cancellationToken)
    {
        if (await directory.FindIdAsync(locator, cancellationToken) is not { } environmentId)
        {
            return AccessFailures.NotFound;
        }

        return await publications.FindAsync(environmentId, version, cancellationToken) is { } publication
            ? publication.Flags.ToList()
            : PublishingFailures.PublicationNotFound;
    }
}
