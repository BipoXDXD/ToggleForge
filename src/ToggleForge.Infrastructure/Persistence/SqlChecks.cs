namespace ToggleForge.Infrastructure.Persistence;

/// <summary>As mesmas regras dos value objects, repetidas no schema para dado gravado por fora da aplicação.</summary>
internal static class SqlChecks
{
    public static string Key(string column) => $"{column} ~ '^[a-z0-9][a-z0-9._-]{{0,63}}$'";

    public static string DisplayName(string column) => $"char_length({column}) between 1 and 100";

    public static string BasisPoints(string column) => $"{column} between 0 and 10000";

    public static string FlagType(string column) => $"{column} in ('boolean', 'json')";

    /// <summary>Uma flag booleana só guarda os literais true e false (ADR 0011).</summary>
    public static string BooleanValues(string type, string onValue, string offValue) =>
        $"{type} <> 'boolean' or (json_typeof({onValue}) = 'boolean' and json_typeof({offValue}) = 'boolean')";
}
