namespace ToggleForge.Api.Tests.Support;

public static class Paths
{
    public const string AntiforgeryToken = "/api/v1/antiforgery-token";
    public const string Accounts = "/api/v1/accounts";
    public const string Session = "/api/v1/session";
    public const string Snapshot = "/sdk/v1/snapshot";

    public static string Projects(Guid organizationId) => $"/api/v1/organizations/{organizationId}/projects";

    public static string Project(Guid organizationId, string projectKey) => $"{Projects(organizationId)}/{projectKey}";

    public static string Environment(Guid organizationId, string projectKey, string environmentKey) =>
        $"{Project(organizationId, projectKey)}/environments/{environmentKey}";
}
