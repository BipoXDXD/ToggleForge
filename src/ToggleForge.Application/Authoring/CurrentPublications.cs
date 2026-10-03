using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

internal static class CurrentPublications
{
    public static async Task<Publication?> FindAsync(
        this IPublicationRepository publications, ProjectEnvironment environment, CancellationToken cancellationToken)
    {
        if (environment.CurrentVersion is not { } version)
        {
            return null;
        }

        return await publications.FindAsync(environment.Id, version, cancellationToken)
            ?? throw new InvalidOperationException($"Environment {environment.Id} points to missing version {version}.");
    }
}
