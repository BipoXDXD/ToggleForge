using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Delivery;

namespace ToggleForge.Api.Endpoints;

internal static class SnapshotEndpoints
{
    public static void MapSnapshotEndpoints(this RouteGroupBuilder sdk) =>
        sdk.MapGet("/snapshot", GetAsync).WithName("getSnapshot");

    /// <summary>O ambiente vem da credencial autenticada; o jogo não escolhe nada (STRIDE S1).</summary>
    private static async Task<Results<Ok<SnapshotResponse>, StatusCodeHttpResult, JsonHttpResult<ProblemBody>>> GetAsync(
        HttpContext context, SnapshotService snapshots, CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        var knownTags = KnownTags(context.Request, errors);
        if (knownTags is null)
        {
            return errors.ToResult();
        }

        var environmentId = Guid.Parse(context.User.FindFirstValue(SdkCredentialHandler.EnvironmentClaim)
            ?? throw new InvalidOperationException("SDK principal without an environment claim."));
        if (!(await snapshots.FindCurrentAsync(environmentId, cancellationToken)).TryGetValue(out var snapshot, out var failure))
        {
            return Problems.From(failure);
        }

        var etag = CacheControl.ETagOf(snapshot.Version);
        if (knownTags.Any(tag => tag.Compare(new EntityTagHeaderValue(etag), useStrongComparison: false)))
        {
            context.Response.Headers.ETag = etag;
            return TypedResults.StatusCode(StatusCodes.Status304NotModified);
        }

        CacheControl.Private(context.Response, etag);
        return TypedResults.Ok(SnapshotResponse.Of(snapshot));
    }

    /// <summary>Nulo quando If-None-Match está malformado; vazio quando o jogo ainda não tem snapshot.</summary>
    private static IList<EntityTagHeaderValue>? KnownTags(HttpRequest request, InputErrors errors)
    {
        var header = request.Headers.IfNoneMatch;
        if (header.Count == 0)
        {
            return [];
        }

        if (EntityTagHeaderValue.TryParseStrictList(header, out var tags))
        {
            return tags;
        }

        errors.InHeader(HeaderNames.IfNoneMatch, "Send the ETag of the last snapshot exactly as received.");
        return null;
    }
}
