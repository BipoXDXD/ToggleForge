namespace ToggleForge.Domain.Authoring;

public sealed record NewProject(Project Project, IReadOnlyList<ProjectEnvironment> Environments);
