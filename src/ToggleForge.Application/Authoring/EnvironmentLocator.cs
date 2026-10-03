using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

/// <summary>Como o console endereça um ambiente: pelas chaves, dentro da organização.</summary>
public sealed record EnvironmentLocator(Guid OrganizationId, ProjectKey ProjectKey, EnvironmentKey EnvironmentKey);
