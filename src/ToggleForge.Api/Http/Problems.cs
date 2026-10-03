using Microsoft.AspNetCore.Http.HttpResults;
using ToggleForge.Domain;

namespace ToggleForge.Api.Http;

internal static class Problems
{
    public const string ContentType = "application/problem+json";

    public static JsonHttpResult<ProblemBody> From(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var (status, title) = failure.Kind switch
        {
            FailureKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            FailureKind.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            FailureKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            FailureKind.PreconditionFailed => (StatusCodes.Status412PreconditionFailed, "Precondition failed"),
            FailureKind.InvalidValue => throw new InvalidOperationException(
                $"'{failure.Code}' is an input error: the endpoint must report it on its field."),
            var unknown => throw new InvalidOperationException($"Unknown failure kind '{unknown}'."),
        };
        return Json(new ProblemBody($"/problems/{failure.Code}", title, status, failure.Detail));
    }

    public static JsonHttpResult<ProblemBody> InvalidRequest(IReadOnlyList<ProblemError> errors) =>
        Json(new ProblemBody(
            "/problems/invalid-request",
            "Invalid request",
            StatusCodes.Status400BadRequest,
            "Fix the listed problems and send the request again.",
            Errors: errors));

    public static JsonHttpResult<ProblemBody> Unauthorized(string detail) =>
        Json(new ProblemBody("/problems/unauthorized", "Unauthorized", StatusCodes.Status401Unauthorized, detail));

    public static JsonHttpResult<ProblemBody> TooManyRequests(string detail) =>
        Json(new ProblemBody("/problems/too-many-requests", "Too many requests", StatusCodes.Status429TooManyRequests, detail));

    public static JsonHttpResult<ProblemBody> Json(ProblemBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return TypedResults.Json(body, contentType: ContentType, statusCode: body.Status);
    }
}
