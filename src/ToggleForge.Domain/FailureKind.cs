namespace ToggleForge.Domain;

public enum FailureKind
{
    NotFound,
    Forbidden,
    Conflict,

    /// <summary>A versão que o cliente leu não é mais a atual (If-Match desatualizado).</summary>
    PreconditionFailed,
}
