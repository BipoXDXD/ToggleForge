using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed record DraftChange(FlagKey FlagKey, DraftChangeKind Kind);
