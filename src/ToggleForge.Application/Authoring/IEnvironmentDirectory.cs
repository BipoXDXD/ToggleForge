namespace ToggleForge.Application.Authoring;

/// <summary>API pública do módulo Authoring: como outros módulos acham um ambiente.</summary>
public interface IEnvironmentDirectory
{
    Task<Guid?> FindIdAsync(EnvironmentLocator locator, CancellationToken cancellationToken);
}
