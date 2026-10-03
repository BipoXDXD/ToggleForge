namespace ToggleForge.Domain.Authoring;

public static class AuthoringFailures
{
    public static Failure ProjectKeyTaken { get; } =
        new(FailureKind.Conflict, "key-taken", "The organization already has a project with this key.");

    public static Failure ProjectLimitReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"An organization can have at most {Project.MaxPerOrganization} projects.");

    public static Failure FlagKeyTaken { get; } =
        new(FailureKind.Conflict, "key-taken", "The draft already has a flag with this key.");

    public static Failure FlagLimitReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"A draft can have at most {ProjectEnvironment.MaxFlags} flags.");

    public static Failure DraftChanged { get; } =
        new(FailureKind.Conflict, "draft-changed", "The draft changed after it was reviewed. Reload it and review again.");

    public static Failure NothingToPublish { get; } =
        new(FailureKind.Conflict, "nothing-to-publish", "The draft is identical to the current publication.");
}
