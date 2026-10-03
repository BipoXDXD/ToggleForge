namespace ToggleForge.Domain;

public enum FailureKind
{
    NotFound,
    Forbidden,
    Conflict,

    /// <summary>A versão que o cliente leu não é mais a atual (If-Match desatualizado).</summary>
    PreconditionFailed,

    /// <summary>Um campo bem formado que uma regra fora da borda recusou; o endpoint aponta o campo.</summary>
    InvalidValue,
}
