using ToggleForge.Application.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Endpoints;

/// <summary>URLs canônicas para o header Location, montadas dos valores já convertidos e não do que o cliente digitou.</summary>
internal static class ResourcePaths
{
    public static string Project(Guid organizationId, ProjectKey key) =>
        $"/api/v1/organizations/{organizationId:D}/projects/{key.Value}";

    public static string Flag(EnvironmentLocator locator, FlagKey key) => $"{Environment(locator)}/draft/flags/{key.Value}";

    public static string Publications(EnvironmentLocator locator) => $"{Environment(locator)}/publications";

    public static string Publication(EnvironmentLocator locator, long version) => $"{Publications(locator)}/{version}";

    public static string Environment(EnvironmentLocator locator) =>
        $"{Project(locator.OrganizationId, locator.ProjectKey)}/environments/{locator.EnvironmentKey.Value}";
}
