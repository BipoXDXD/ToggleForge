namespace ToggleForge.Infrastructure.Access;

internal enum PasswordExposure
{
    NotFound,

    Exposed,

    /// <summary>O serviço não respondeu a tempo ou falhou; o cadastro segue sem a checagem (fail-open, ADR 0007).</summary>
    Unknown,
}
