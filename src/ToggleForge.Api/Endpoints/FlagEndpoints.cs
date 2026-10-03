using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Api.Endpoints;

/// <summary>Uma flag do rascunho. A ETag é a revisão em que ela mudou por último, então editar uma flag não invalida as outras.</summary>
internal static class FlagEndpoints
{
    private const string FlagPath = "/draft/flags/{flagKey}";
    private const string MergePatch = "application/merge-patch+json";

    public static void MapFlagEndpoints(this RouteGroupBuilder environment)
    {
        environment.MapGet(FlagPath, GetAsync).WithName("getFlag");
        environment.MapPatch(FlagPath, ChangeAsync).WithName("updateFlag").Accepts<FlagModificationRequest>(MergePatch)
            .AddEndpointFilter<AntiforgeryFilter>();
        environment.MapDelete(FlagPath, RemoveAsync).WithName("deleteFlag").AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<Results<Ok<FlagResponse>, JsonHttpResult<ProblemBody>>> GetAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        string flagKey,
        HttpContext context,
        DraftService drafts,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = RouteInput.Flag(flagKey, errors);
        if (errors.Any || locator is null || key is null)
        {
            return errors.ToResult();
        }

        if (!(await drafts.GetFlagAsync(context.User.MemberId(), locator, key, cancellationToken)).TryGetValue(out var flag, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response, CacheControl.ETagOf(flag.Revision));
        return TypedResults.Ok(FlagResponse.Of(flag.Key, flag.Settings));
    }

    private static async Task<Results<Ok<FlagResponse>, JsonHttpResult<ProblemBody>>> ChangeAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        string flagKey,
        FlagModificationRequest request,
        HttpContext context,
        DraftService drafts,
        CancellationToken cancellationToken)
    {
        if (IfMatch.IsMissing(context.Request))
        {
            return IfMatch.Required();
        }

        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = RouteInput.Flag(flagKey, errors);
        var expectedRevision = IfMatch.ExpectedRevision(context.Request, errors);
        var change = Change(request, errors);
        if (errors.Any || locator is null || key is null || expectedRevision is null || change is null)
        {
            return errors.ToResult();
        }

        var changed = await drafts.ChangeFlagAsync(context.User.MemberId(), locator, key, expectedRevision.Value, change, cancellationToken);
        if (!changed.TryGetValue(out var flag, out var failure))
        {
            return Problems.From(failure);
        }

        context.Response.Headers.ETag = CacheControl.ETagOf(flag.Revision);
        return TypedResults.Ok(FlagResponse.Of(flag.Key, flag.Settings));
    }

    private static async Task<Results<NoContent, JsonHttpResult<ProblemBody>>> RemoveAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        string flagKey,
        HttpContext context,
        DraftService drafts,
        CancellationToken cancellationToken)
    {
        if (IfMatch.IsMissing(context.Request))
        {
            return IfMatch.Required();
        }

        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = RouteInput.Flag(flagKey, errors);
        var expectedRevision = IfMatch.ExpectedRevision(context.Request, errors);
        if (errors.Any || locator is null || key is null || expectedRevision is null)
        {
            return errors.ToResult();
        }

        var removed = await drafts.RemoveFlagAsync(context.User.MemberId(), locator, key, expectedRevision.Value, cancellationToken);
        return removed.TryGetValue(out _, out var failure) ? TypedResults.NoContent() : Problems.From(failure);
    }

    private static FlagChange? Change(FlagModificationRequest request, InputErrors errors)
    {
        if (request.IsEmpty)
        {
            errors.InBody(string.Empty, "Send at least one field to change.");
            return null;
        }

        var rollout = request.RolloutBasisPoints.IsPresent ? FlagInput.Rollout(request.RolloutBasisPoints.Value, errors) : null;
        if (request.RolloutBasisPoints.IsPresent && rollout is null)
        {
            return null;
        }

        return new FlagChange(request.Enabled.OrNull, request.OnValue.OrNull, request.OffValue.OrNull, rollout);
    }
}
