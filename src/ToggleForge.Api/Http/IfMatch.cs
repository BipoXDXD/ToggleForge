using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Net.Http.Headers;

namespace ToggleForge.Api.Http;

/// <summary>
/// A revisão que o cliente leu, vinda do If-Match (DWA cap. 12). O servidor só emite ETags fortes com a
/// revisão, então aceita exatamente uma delas; lista, curinga e ETag fraca são recusados como inválidos.
/// </summary>
internal static class IfMatch
{
    public static bool IsMissing(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.IfMatch.Count == 0;
    }

    public static long? ExpectedRevision(HttpRequest request, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        if (EntityTagHeaderValue.TryParseStrictList(request.Headers.IfMatch, out var tags)
            && tags is [{ IsWeak: false } tag]
            && long.TryParse(tag.Tag.AsSpan().Trim('"'), System.Globalization.NumberStyles.None, null, out var revision)
            && revision >= 1)
        {
            return revision;
        }

        errors.InHeader(HeaderNames.IfMatch, "Send the ETag of the flag exactly as you received it.");
        return null;
    }

    public static JsonHttpResult<ProblemBody> Required() =>
        Problems.Json(new ProblemBody(
            "/problems/precondition-required",
            "Precondition required",
            StatusCodes.Status428PreconditionRequired,
            "Send If-Match with the ETag of the flag you read, so a concurrent change is not overwritten."));
}
