using ToggleForge.Domain;

namespace ToggleForge.Application.Delivery;

public static class CredentialFailures
{
    /// <summary>Inexistente ou de outro ambiente: as duas respondem igual.</summary>
    public static Failure CredentialNotFound { get; } =
        new(FailureKind.NotFound, "not-found", "The environment has no credential with this id.");
}
