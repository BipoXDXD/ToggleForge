using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Endpoints;

internal static class SimulationEndpoints
{
    private const string VersionPointer = "/version";

    public static void MapSimulationEndpoints(this RouteGroupBuilder environment) =>
        environment.MapPost("/simulations", SimulateAsync).WithName("simulatePlayer").AddEndpointFilter<AntiforgeryFilter>();

    /// <summary>
    /// O ID do jogador vem no corpo porque pode ser dado pessoal, e a resposta é no-store pelo mesmo
    /// motivo. Versão inexistente é erro de entrada, como no rollback: o ambiente do path existe.
    /// </summary>
    private static async Task<Results<Ok<SimulationResponse>, JsonHttpResult<ProblemBody>>> SimulateAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        SimulationRequest request,
        HttpContext context,
        SimulationService simulations,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var playerId = errors.Parse<PlayerId>(
            request.PlayerId,
            "/playerId",
            PlayerId.TryParse,
            $"Use 1 to {PlayerId.MaxLength} characters, not all blank, without control characters or unpaired surrogates.");
        var target = Target(request, errors);
        if (errors.Any || locator is null || playerId is null || target is null)
        {
            return errors.ToResult();
        }

        var simulated = await simulations.SimulateAsync(context.User.MemberId(), locator, target.Version, playerId, cancellationToken);
        if (simulated.TryGetValue(out var simulation, out var failure))
        {
            CacheControl.NoStore(context.Response);
            return TypedResults.Ok(SimulationResponse.Of(simulation));
        }

        if (failure == PublishingFailures.PublicationNotFound)
        {
            errors.InBody(VersionPointer, failure.Detail);
            return errors.ToResult();
        }

        return Problems.From(failure);
    }

    /// <summary>O rascunho não tem versão; a publicação exige uma.</summary>
    private static SimulationTarget? Target(SimulationRequest request, InputErrors errors)
    {
        if (request.Source is null)
        {
            errors.MissingInBody("/source");
            return null;
        }

        if (request.Source == SimulationSources.Draft)
        {
            if (request.Version is null)
            {
                return new SimulationTarget(Version: null);
            }

            errors.InBody(VersionPointer, "Leave version out to simulate the draft.");
            return null;
        }

        if (request.Source != SimulationSources.Publication)
        {
            errors.InBody("/source", $"Use {SimulationSources.Draft} or {SimulationSources.Publication}.");
            return null;
        }

        return errors.RequireVersion(request.Version, VersionPointer) is { } version ? new SimulationTarget(version) : null;
    }

    /// <summary><see cref="Version"/> nula é o rascunho.</summary>
    private sealed record SimulationTarget(long? Version);
}
