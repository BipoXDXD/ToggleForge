using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ToggleForge.Api.Authentication;
using ToggleForge.Api.Contracts;
using ToggleForge.Api.Http;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;

namespace ToggleForge.Api.Endpoints;

internal static class PublicationEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const string TargetVersionPointer = "/targetVersion";

    public static void MapPublicationEndpoints(this RouteGroupBuilder environment)
    {
        environment.MapGet("/publications", ListAsync).WithName("listPublications");
        environment.MapPost("/publications", PublishAsync).WithName("publishDraft").AddEndpointFilter<AntiforgeryFilter>();
        environment.MapGet("/publications/{version}", GetAsync).WithName("getPublication");
        environment.MapPost("/rollbacks", RollBackAsync).WithName("rollBack").AddEndpointFilter<AntiforgeryFilter>();
    }

    private static async Task<Results<Ok<PublicationListResponse>, JsonHttpResult<ProblemBody>>> ListAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        [FromQuery] string? maxPageSize,
        [FromQuery] string? pageToken,
        HttpContext context,
        PublicationHistoryService history,
        PageTokens pageTokens,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        var memberId = context.User.MemberId();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var size = PageSize(maxPageSize, errors);
        var beforeVersion = locator is null ? null : BeforeVersion(pageToken, memberId, locator, pageTokens, errors);

        if (errors.Any || locator is null || size is null)
        {
            return errors.ToResult();
        }

        var listed = await history.ListAsync(memberId, locator, new PageRequest(size.Value, beforeVersion), cancellationToken);
        if (!listed.TryGetValue(out var page, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        var nextPageToken = page.NextBeforeVersion is { } next
            ? pageTokens.Issue(memberId, ResourcePaths.Publications(locator), next)
            : null;
        return TypedResults.Ok(new PublicationListResponse(
            page.Items.Select(PublicationSummaryResponse.Of).ToList(), new PageMetadata(nextPageToken)));
    }

    private static async Task<Results<Ok<PublicationResponse>, JsonHttpResult<ProblemBody>>> GetAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        string version,
        HttpContext context,
        PublicationHistoryService history,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var number = RouteInput.Version(version, errors);
        if (errors.Any || locator is null || number is null)
        {
            return errors.ToResult();
        }

        if (!(await history.GetAsync(context.User.MemberId(), locator, number.Value, cancellationToken)).TryGetValue(out var publication, out var failure))
        {
            return Problems.From(failure);
        }

        CacheControl.Private(context.Response);
        return TypedResults.Ok(PublicationResponse.Of(publication));
    }

    /// <summary>Repetir com a mesma chave e o mesmo corpo devolve a publicação original, também com 201 (ADP cap. 26).</summary>
    private static async Task<Results<Created<PublicationResponse>, JsonHttpResult<ProblemBody>>> PublishAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        PublicationRequest request,
        HttpContext context,
        PublishingService publishing,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = IdempotencyKeyOf(idempotencyKey, errors);
        var command = Command(request, errors);
        if (errors.Any || locator is null || key is null || command is null)
        {
            return errors.ToResult();
        }

        var published = await publishing.PublishAsync(context.User.MemberId(), locator, command, key, cancellationToken);
        return published.TryGetValue(out var publication, out var failure)
            ? TypedResults.Created(ResourcePaths.Publication(locator, publication.Version), PublicationResponse.Of(publication))
            : Problems.From(failure);
    }

    /// <summary>
    /// Como publicar, inclusive na repetição. A versão de destino inexistente é erro de entrada, porque o
    /// recurso do path existe: só o valor do campo não serve.
    /// </summary>
    private static async Task<Results<Created<PublicationResponse>, JsonHttpResult<ProblemBody>>> RollBackAsync(
        string organizationId,
        string projectKey,
        string environmentKey,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        RollbackRequest request,
        HttpContext context,
        PublishingService publishing,
        CancellationToken cancellationToken)
    {
        var errors = new InputErrors();
        RouteInput.TryLocator(organizationId, projectKey, environmentKey, errors, out var locator);
        var key = IdempotencyKeyOf(idempotencyKey, errors);
        var command = Command(request, errors);
        if (errors.Any || locator is null || key is null || command is null)
        {
            return errors.ToResult();
        }

        var rolledBack = await publishing.RollBackAsync(context.User.MemberId(), locator, command, key, cancellationToken);
        if (rolledBack.TryGetValue(out var publication, out var failure))
        {
            return TypedResults.Created(ResourcePaths.Publication(locator, publication.Version), PublicationResponse.Of(publication));
        }

        if (failure == AuthoringFailures.RollbackTargetNotFound)
        {
            errors.InBody(TargetVersionPointer, failure.Detail);
            return errors.ToResult();
        }

        return Problems.From(failure);
    }

    /// <summary>O token só vale na coleção para a qual foi emitido, identificada pelo path canônico.</summary>
    private static long? BeforeVersion(
        string? pageToken, Guid memberId, EnvironmentLocator locator, PageTokens pageTokens, InputErrors errors)
    {
        if (pageToken is null)
        {
            return null;
        }

        if (pageTokens.TryRead(pageToken, memberId, ResourcePaths.Publications(locator), out var cursor))
        {
            return cursor;
        }

        errors.InQuery(nameof(pageToken), "Use the nextPageToken of a page you listed in the last 60 minutes, or leave it out.");
        return null;
    }

    private static int? PageSize(string? value, InputErrors errors)
    {
        if (value is null)
        {
            return PageRequest.DefaultSize;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size is >= 1 and <= PageRequest.MaxSize)
        {
            return size;
        }

        errors.InQuery("maxPageSize", $"Use an integer from 1 to {PageRequest.MaxSize}, or leave it out for {PageRequest.DefaultSize}.");
        return null;
    }

    private static IdempotencyKey? IdempotencyKeyOf(string? value, InputErrors errors)
    {
        if (value is null)
        {
            errors.InHeader(IdempotencyKeyHeader, "The header is required.", ErrorTypes.MissingValue);
            return null;
        }

        if (IdempotencyKey.TryParse(value, out var key))
        {
            return key;
        }

        errors.InHeader(
            IdempotencyKeyHeader,
            $"Use {IdempotencyKey.MinLength} to {IdempotencyKey.MaxLength} characters from [A-Za-z0-9_-], such as a UUID.");
        return null;
    }

    private static PublishCommand? Command(PublicationRequest request, InputErrors errors)
    {
        ChangeSummary? summary = null;
        if (request.Summary is not null && !ChangeSummary.TryParse(request.Summary, out summary))
        {
            errors.InBody("/summary", $"Use 1 to {ChangeSummary.MaxLength} characters without control characters, or leave it out.");
        }

        if (!errors.Require(request.DraftRevision, "/draftRevision"))
        {
            return null;
        }

        if (request.DraftRevision.Value < 1)
        {
            errors.InBody("/draftRevision", "Use the revision of the draft you reviewed, starting at 1.");
            return null;
        }

        return new PublishCommand(request.DraftRevision.Value, summary);
    }

    private static RollbackCommand? Command(RollbackRequest request, InputErrors errors)
    {
        var target = Version(request.TargetVersion, TargetVersionPointer, errors);
        var expectedCurrent = Version(request.ExpectedCurrentVersion, "/expectedCurrentVersion", errors);
        return target is null || expectedCurrent is null
            ? null
            : new RollbackCommand(target.Value, expectedCurrent.Value, request.DiscardDraftChanges ?? false);
    }

    private static long? Version(long? value, string pointer, InputErrors errors)
    {
        if (!errors.Require(value, pointer))
        {
            return null;
        }

        if (value.Value < 1)
        {
            errors.InBody(pointer, "Versions start at 1.");
            return null;
        }

        return value;
    }
}
