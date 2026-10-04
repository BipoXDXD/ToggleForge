using System.Text.Json;
using ToggleForge.Domain;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Http;

internal static class FlagInput
{
    /// <summary>
    /// Teto do corpo de <c>createFlag</c> e <c>updateFlag</c> (canvas D3). Dois valores de 4 KiB compactados somam 8 KiB e
    /// deixam 24 KiB para os espaços e escapes comuns. Não cabe qualquer escrita: cada <c>\uXXXX</c> ocupa 6 bytes, então
    /// um valor todo escapado chega a 6 vezes o compactado (24 KiB) e dois deles (48 KiB) passam do teto. Quem aplica é
    /// o servidor (Kestrel), antes do binding.
    /// </summary>
    public const int BodyLimitBytes = 32 * 1024;

    public const string RolloutPointer = "/rolloutBasisPoints";
    public const string OnValuePointer = "/onValue";
    public const string OffValuePointer = "/offValue";

    public static RolloutPercentage? Rollout(int basisPoints, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (RolloutPercentage.TryFromBasisPoints(basisPoints, out var rollout))
        {
            return rollout;
        }

        errors.InBody(RolloutPointer, "Use 0 to 10000 basis points (100 = 1%).");
        return null;
    }

    /// <returns>Nulo quando o valor falta ou é inválido; o erro já foi apontado no campo.</returns>
    public static JsonFlagValue? Value(JsonElement element, string pointer, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (element.ValueKind == JsonValueKind.Undefined)
        {
            errors.MissingInBody(pointer);
            return null;
        }

        if (JsonFlagValue.TryParse(RawTextOf(element), out var value, out var problem))
        {
            return value;
        }

        errors.InBody(pointer, Detail(problem));
        return null;
    }

    /// <summary>
    /// Aponta no campo o valor que não serve ao tipo da flag. Falso para as outras falhas, que seguem como problem do
    /// recurso (404, 403, 409...).
    /// </summary>
    public static bool TryReportValueNotOfFlagType(Failure failure, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(errors);

        var pointer = failure == AuthoringFailures.OnValueNotOfFlagType ? OnValuePointer
            : failure == AuthoringFailures.OffValueNotOfFlagType ? OffValuePointer
            : null;
        if (pointer is null)
        {
            return false;
        }

        errors.InBody(pointer, failure.Detail);
        return true;
    }

    /// <returns>Nulo quando o corpo traz UTF-8 inválido dentro do valor, que o binding só descobre ao transcodificar.</returns>
    private static string? RawTextOf(JsonElement element)
    {
        try
        {
            return element.GetRawText();
        }
        catch (InvalidOperationException)
        {
            // Bytes que não são UTF-8 não formam um valor JSON; TryParse recebe nulo e responde NotJson.
            return null;
        }
    }

    private static string Detail(JsonValueProblem problem) => problem switch
    {
        JsonValueProblem.NotJson => "The value is not a JSON value.",
        JsonValueProblem.DuplicateKey => "The value repeats a key in an object.",
        JsonValueProblem.TooDeep => $"The value nests deeper than {JsonFlagValue.MaxDepth} levels.",
        JsonValueProblem.NumberOutOfRange => "The value has a number out of the range of a double.",
        JsonValueProblem.TooLarge => $"The value is over {JsonFlagValue.MaxBytes / 1024} KiB once compacted.",
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown json value problem."),
    };
}
