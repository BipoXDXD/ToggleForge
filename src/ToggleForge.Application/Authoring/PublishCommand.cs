using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public sealed record PublishCommand(long ReviewedDraftRevision, ChangeSummary? Summary);
