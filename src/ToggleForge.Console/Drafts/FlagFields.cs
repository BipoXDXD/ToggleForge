using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>Como os formulários de flag leem o que o membro digitou antes de mandar para a API.</summary>
internal static class FlagFields
{
    public const string InvalidRollout = "Enter a rollout between 0 and 100, with at most two decimals.";

    public const string NullOnlyAtCreation = "null is accepted only when the flag is created. Use another value, such as {} or \"\".";

    public const string TooDeep = "This value nests too deep. JSON values can have at most 8 levels.";

    /// <returns>Os pontos-base, ou nulo com o erro no campo do rollout.</returns>
    public static int? Rollout(decimal percent, FieldErrors errors)
    {
        if (RolloutShare.TryToBasisPoints(percent, out var basisPoints))
        {
            return basisPoints;
        }

        errors.Add(FieldErrors.Rollout, InvalidRollout);
        return null;
    }

    /// <param name="field"><see cref="FieldErrors.OnValue"/> ou <see cref="FieldErrors.OffValue"/>.</param>
    /// <returns>O valor compacto, ou nulo com o erro no <paramref name="field"/>.</returns>
    /// <remarks>
    /// O teto de 4 KiB é conferido aqui para o membro não esperar a API, mas é dela a palavra final (ADR 0011); a
    /// profundidade fica só com ela. A chave repetida também é regra dela, mas a API a recusa ao ler o corpo, com um
    /// erro que não diz o campo; conferida aqui, a mensagem cai sob a área certa.
    /// </remarks>
    public static FlagValue? Value(string text, string field, FieldErrors errors)
    {
        var analysis = JsonText.Analyze(text);
        if (analysis is JsonAnalysis.Valid { Level: not MeterLevel.OverLimit } valid && !JsonText.RepeatsAKey(valid.Value))
        {
            return valid.Value;
        }

        errors.Add(field, analysis switch
        {
            JsonAnalysis.Empty => $"Enter a JSON value for {AudienceOf(field)}.",
            JsonAnalysis.TooDeep => TooDeep,
            JsonAnalysis.Valid { Level: MeterLevel.OverLimit } =>
                $"The value for {AudienceOf(field)} is over 4 KiB once compacted. Make it smaller: every player downloads it.",
            JsonAnalysis.Valid => $"The value for {AudienceOf(field)} repeats a key in an object. Each key can appear only once.",
            _ => NotJson(field),
        });
        return null;
    }

    internal static string NotJson(string field) => $"The value for {AudienceOf(field)} is not valid JSON.";

    public static string NearLimit(string field) => $"The value for {AudienceOf(field)} is near the limit.";

    public static string OverLimit(string field) => $"The value for {AudienceOf(field)} is over the limit.";

    private static string AudienceOf(string field) => field switch
    {
        FieldErrors.OnValue => "included players",
        FieldErrors.OffValue => "everyone else",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Only the two values of a flag have an audience."),
    };
}
