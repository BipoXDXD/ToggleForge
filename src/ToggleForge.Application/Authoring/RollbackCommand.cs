namespace ToggleForge.Application.Authoring;

public sealed record RollbackCommand(long TargetVersion, long ExpectedCurrentVersion, bool DiscardDraftChanges);
