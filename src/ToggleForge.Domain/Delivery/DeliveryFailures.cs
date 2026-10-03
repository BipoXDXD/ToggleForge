namespace ToggleForge.Domain.Delivery;

public static class DeliveryFailures
{
    public static Failure CredentialLimitReached { get; } = new(
        FailureKind.Conflict,
        "limit-reached",
        $"An environment can have at most {Credential.MaxActivePerEnvironment} active credentials. Revoke one first.");
}
