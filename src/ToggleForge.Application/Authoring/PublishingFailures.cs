using ToggleForge.Domain;

namespace ToggleForge.Application.Authoring;

public static class PublishingFailures
{
    public static Failure IdempotencyKeyReused { get; } = new(
        FailureKind.Conflict,
        "idempotency-key-reused",
        "This Idempotency-Key was already used with a different request. Generate a new key for a new request.");
}
