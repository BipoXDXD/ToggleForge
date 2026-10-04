using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Endpoints;

internal static class DraftEndpoints
{
    public static void MapDraftEndpoints(this RouteGroupBuilder environment)
    {
        environment.MapGet("/draft", GetAsync).WithName("getDraft");
        environment.MapPost("/draft/flags", CreateFlagAsync).WithName("createFlag")
            .WithMetadata(new RequestSizeLimitAttribute(FlagInput.BodyLimitBytes))
            .AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<Results<Ok<DraftResponse>, JsonHttpResult<ProblemBody>>> GetAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        HttpContext context,
        DraftService drafts,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        if (!RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator))
        {
            return errors.ToResult();
        }

        if (!(await drafts.GetAsync(context.User.MemberId(), locator, cancellationToken)).TryGetValue(out var draft, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response, CacheControl.ETagOf(draft.Revision));
        return TypedResults.Ok(DraftResponse.Of(draft));
    }

    private static async Task<Results<Created<FlagResponse>, JsonHttpResult<ProblemBody>>> CreateFlagAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        FlagCreationRequest request,
        HttpContext context,
        DraftService drafts,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = errors.Parse<FlagKey>(request.Key, "/key", FlagKey.TryParse, InputDetails.Key);
        var settings = Settings(request, errors);
        if (errors.Any || locator is null || key is null || settings is null)
        {
            return errors.ToResult();
        }

        var created = await drafts.CreateFlagAsync(context.User.MemberId(), locator, key, settings, cancellationToken);
        if (!created.TryGetValue(out var flag, out var failure))
        {
            return Problems.From(failure);
        }

        context.Response.Headers.ETag = CacheControl.ETagOf(flag.Revision);
        return TypedResults.Created(ResourcePaths.Flag(locator, key), FlagResponse.Of(flag.Key, flag.Settings));
    }

    private static FlagSettings? Settings(FlagCreationRequest request, InputErrors errors)
    {
        errors.Require(request.Enabled, "/enabled");
        var type = Type(request.Type, errors);
        var onValue = FlagInput.Value(request.OnValue, FlagInput.OnValuePointer, errors);
        var offValue = FlagInput.Value(request.OffValue, FlagInput.OffValuePointer, errors);
        var rollout = Rollout(request.RolloutBasisPoints, errors);
        // Só com o tipo e os dois valores lidos: com um deles faltando, o par não tem o que conferir.
        var values = type is { } flagType && onValue is not null && offValue is not null ? Values(flagType, onValue, offValue, errors) : null;
        return request.Enabled is { } enabled && values is not null && rollout is not null
            ? new FlagSettings(enabled, values, rollout)
            : null;
    }

    /// <summary>Ausente é booleano, para os clientes de antes das flags JSON; <c>null</c> é um tipo inválido.</summary>
    private static FlagType? Type(JsonElement type, InputErrors errors)
    {
        if (type.ValueKind == JsonValueKind.Undefined)
        {
            return FlagType.Boolean;
        }

        if (FlagTypes.TryParse(type, out var parsed))
        {
            return parsed;
        }

        errors.InBody("/type", "Use boolean or json.");
        return null;
    }

    private static FlagValues? Values(FlagType type, JsonFlagValue onValue, JsonFlagValue offValue, InputErrors errors)
    {
        if (FlagValues.Of(type, onValue, offValue).TryGetValue(out var values, out var failure))
        {
            return values;
        }

        return FlagInput.TryReportValueNotOfFlagType(failure, errors)
            ? null
            : throw new InvalidOperationException($"FlagValues.Of failed with the unexpected '{failure.Code}'.");
    }

    private static RolloutPercentage? Rollout(int? basisPoints, InputErrors errors) =>
        errors.Require(basisPoints, FlagInput.RolloutPointer) ? FlagInput.Rollout(basisPoints.Value, errors) : null;
}
