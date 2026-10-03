using ToggleForge.Domain;

namespace ToggleForge.Application.Delivery;

public static class SnapshotFailures
{
    public static Failure NoPublication { get; } =
        new(FailureKind.NotFound, "no-publication", "This environment has no publication yet. Publish its draft first.");
}
