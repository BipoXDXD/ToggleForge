using ToggleForge.Domain;

namespace ToggleForge.Application.Access;

public static class AccessFailures
{
    /// <summary>Também para recursos de organizações das quais o membro não participa (SA cap. 4).</summary>
    public static Failure NotFound { get; } = new(FailureKind.NotFound, "not-found", "The resource does not exist.");

    public static Failure Forbidden { get; } =
        new(FailureKind.Forbidden, "forbidden", "Your role in this organization does not allow this operation.");

    public static Failure EmailTaken { get; } =
        new(FailureKind.Conflict, "email-taken", "An account with this email already exists. Sign in instead.");

    public static Failure PasswordBreached { get; } =
        new(FailureKind.InvalidValue, "password-breached", "This password appears in known data breaches. Choose another one.");
}
