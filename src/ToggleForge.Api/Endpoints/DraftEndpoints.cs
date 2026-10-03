using Microsoft.AspNetCore.Http.HttpResults;
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
        environment.MapPost("/draft/flags", CreateFlagAsync).WithName("createFlag").AddEndpointFilter<AntiforgeryFilter>();
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
        errors.Require(request.OnValue, "/onValue");
        errors.Require(request.OffValue, "/offValue");
        var rollout = Rollout(request.RolloutBasisPoints, errors);
        return request is { Enabled: { } enabled, OnValue: { } onValue, OffValue: { } offValue } && rollout is not null
            ? new FlagSettings(enabled, onValue, offValue, rollout)
            : null;
    }

    private static RolloutPercentage? Rollout(int? basisPoints, InputErrors errors)
    {
        const string pointer = "/rolloutBasisPoints";
        if (!errors.Require(basisPoints, pointer))
        {
            return null;
        }

        if (RolloutPercentage.TryFromBasisPoints(basisPoints.Value, out var rollout))
        {
            return rollout;
        }

        errors.InBody(pointer, "Use 0 to 10000 basis points (100 = 1%).");
        return null;
    }
}
