using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ToggleForge.Application.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Api.Http;

internal static class RouteInput
{
    public static bool TryOrganization(string organizationId, InputErrors errors, out Guid id)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (Guid.TryParseExact(organizationId, "D", out id))
        {
            return true;
        }

        errors.InPath(nameof(organizationId), "The organization id is a UUID.");
        return false;
    }

    public static Guid? Credential(string credentialId, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (Guid.TryParseExact(credentialId, "D", out var id))
        {
            return id;
        }

        errors.InPath(nameof(credentialId), "The credential id is a UUID.");
        return null;
    }

    public static ProjectKey? Project(string projectKey, InputErrors errors) =>
        Parse<ProjectKey>(ProjectKey.TryParse(projectKey, out var key), key, nameof(projectKey), errors);

    public static EnvironmentKey? Environment(string environmentKey, InputErrors errors) =>
        Parse<EnvironmentKey>(EnvironmentKey.TryParse(environmentKey, out var key), key, nameof(environmentKey), errors);

    public static FlagKey? Flag(string flagKey, InputErrors errors) =>
        Parse<FlagKey>(FlagKey.TryParse(flagKey, out var key), key, nameof(flagKey), errors);

    public static long? Version(string version, InputErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (long.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
        {
            return parsed;
        }

        errors.InPath(nameof(version), "Versions are integers starting at 1.");
        return null;
    }

    public static bool TryLocator(
        string organizationId,
        string projectKey,
        string environmentKey,
        InputErrors errors,
        [NotNullWhen(true)] out EnvironmentLocator? locator)
    {
        TryOrganization(organizationId, errors, out var organization);
        var project = Project(projectKey, errors);
        var environment = Environment(environmentKey, errors);
        locator = project is not null && environment is not null && !errors.Any
            ? new EnvironmentLocator(organization, project, environment)
            : null;
        return locator is not null;
    }

    private static T? Parse<T>(bool parsed, T? value, string name, InputErrors errors)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (!parsed)
        {
            errors.InPath(name, InputDetails.Key);
        }

        return value;
    }
}
