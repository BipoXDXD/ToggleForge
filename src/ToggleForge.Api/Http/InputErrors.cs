using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ToggleForge.Api.Http;

internal delegate bool TryParser<T>(string? value, [NotNullWhen(true)] out T? result);

/// <summary>
/// Junta todos os problemas da requisição para responder de uma vez (DWA cap. 9): o cliente corrige tudo
/// numa ida só.
/// </summary>
internal sealed class InputErrors
{
    private readonly List<ProblemError> _errors = [];

    public bool Any => _errors.Count > 0;

    public void InBody(string pointer, string detail, string type = ErrorTypes.InvalidValue) =>
        _errors.Add(new ProblemError(type, detail, new ProblemSource("body", Pointer: pointer)));

    public void MissingInBody(string pointer) => InBody(pointer, "The value is required.", ErrorTypes.MissingValue);

    public void InPath(string name, string detail) =>
        _errors.Add(new ProblemError(ErrorTypes.InvalidValue, detail, new ProblemSource("path", Name: name)));

    public void InHeader(string name, string detail, string type = ErrorTypes.InvalidValue) =>
        _errors.Add(new ProblemError(type, detail, new ProblemSource("header", Name: name)));

    /// <summary>Valor obrigatório do corpo; registra o erro quando falta.</summary>
    public bool Require<T>([NotNullWhen(true)] T? value, string pointer)
        where T : struct
    {
        if (value is null)
        {
            MissingInBody(pointer);
        }

        return value is not null;
    }

    /// <summary>Texto obrigatório do corpo convertido no tipo de domínio; nulo quando falta ou é inválido.</summary>
    public T? Parse<T>(string? value, string pointer, TryParser<T> parse, string detail)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(parse);

        if (value is null)
        {
            MissingInBody(pointer);
            return null;
        }

        if (parse(value, out var parsed))
        {
            return parsed;
        }

        InBody(pointer, detail);
        return null;
    }

    public JsonHttpResult<ProblemBody> ToResult() => Problems.InvalidRequest(_errors);
}

internal static class ErrorTypes
{
    public const string InvalidValue = "INVALID_VALUE";
    public const string MissingValue = "MISSING_VALUE";
    public const string InvalidBody = "INVALID_BODY";
    public const string InvalidToken = "INVALID_TOKEN";
}

internal static class InputDetails
{
    public const string Key = "Keys have 1 to 64 characters from [a-z0-9._-] and start with a letter or digit.";
    public const string DisplayName = "Use 1 to 100 characters, not all blank, without control characters.";
}
