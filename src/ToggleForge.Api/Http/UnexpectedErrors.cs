using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ToggleForge.Api.Http;

/// <summary>
/// Requisição malformada vira 400 sem detalhes de .NET; o resto vira 500 com uma referência opaca, e o
/// detalhe só vai para o log (SA cap. 5).
/// </summary>
internal static partial class UnexpectedErrors
{
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is BadHttpRequestException badRequest)
        {
            await BadRequest(badRequest).ExecuteAsync(context);
            return;
        }

        var reference = Guid.NewGuid().ToString("N");
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UnexpectedErrors));
        LogUnexpected(logger, reference, exception);
        await Problems.Json(new ProblemBody(
                "/problems/unexpected", "Unexpected error", StatusCodes.Status500InternalServerError, Instance: $"/unexpected-errors/{reference}"))
            .ExecuteAsync(context);
    }

    private static JsonHttpResult<ProblemBody> BadRequest(BadHttpRequestException exception)
    {
        var errors = new InputErrors();
        var pointer = exception.InnerException is JsonException { Path: { } path } ? ToPointer(path) : string.Empty;
        errors.InBody(pointer, "The body is missing, is not valid JSON, has a null value or has a field this operation does not accept.", ErrorTypes.InvalidBody);
        return errors.ToResult();
    }

    /// <summary>"$.flags[0].key" → "/flags/0/key".</summary>
    private static string ToPointer(string jsonPath) =>
        jsonPath.TrimStart('$').Replace("[", ".", StringComparison.Ordinal).Replace("]", string.Empty, StringComparison.Ordinal).Replace('.', '/');

    [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error {Reference}")]
    private static partial void LogUnexpected(ILogger logger, string reference, Exception? exception);
}
