namespace ToggleForge.Domain.Authoring;

public static class AuthoringFailures
{
    public static Failure ProjectKeyTaken { get; } =
        new(FailureKind.Conflict, "key-taken", "The organization already has a project with this key.");

    public static Failure ProjectLimitReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"An organization can have at most {Project.MaxPerOrganization} projects.");

    public static Failure EnvironmentKeyTaken { get; } =
        new(FailureKind.Conflict, "key-taken", "The project already has an environment with this key.");

    public static Failure EnvironmentLimitReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"A project can have at most {Project.MaxEnvironments} environments.");

    public static Failure FlagKeyTaken { get; } =
        new(FailureKind.Conflict, "key-taken", "The draft already has a flag with this key.");

    public static Failure FlagLimitReached { get; } =
        new(FailureKind.Conflict, "limit-reached", $"A draft can have at most {ProjectEnvironment.MaxFlags} flags.");

    public static Failure FlagNotFound { get; } =
        new(FailureKind.NotFound, "not-found", "The draft has no flag with this key.");

    /// <summary>A flag mudou depois da leitura que deu a ETag enviada em If-Match.</summary>
    public static Failure FlagChanged { get; } =
        new(FailureKind.PreconditionFailed, "flag-changed", "The flag changed after you read it. Read it again and reapply your change.");

    public static Failure DraftChanged { get; } =
        new(FailureKind.Conflict, "draft-changed", "The draft changed after it was reviewed. Reload it and review again.");

    public static Failure NothingToPublish { get; } =
        new(FailureKind.Conflict, "nothing-to-publish", "The draft is identical to the current publication.");

    /// <summary>O ambiente não tem a versão pedida como destino do rollback.</summary>
    public static Failure RollbackTargetNotFound { get; } =
        new(FailureKind.NotFound, "not-found", "The environment has no publication with this version.");

    public static Failure CurrentVersionChanged { get; } =
        new(FailureKind.Conflict, "current-version-changed", "Another publication happened after the version you saw. Reload the history and try again.");

    public static Failure AlreadyCurrent { get; } =
        new(FailureKind.Conflict, "already-current", "The current version already has the content of the target version.");

    public static Failure DraftHasPendingChanges { get; } =
        new(FailureKind.Conflict, "draft-has-pending-changes", "The draft has unpublished changes. Roll back with discardDraftChanges to replace them.");
}
