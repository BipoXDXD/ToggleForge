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

    public static void MapPublicationEndpoints(this RouteGroupBuilder environment) =>
        environment.MapPost("/publications", PublishAsync).WithName("publishDraft").AddEndpointFilter<AntiforgeryFilter>();

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
}
