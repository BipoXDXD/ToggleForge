using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class PublicationRepository(AuthoringDbContext context) : IPublicationRepository
{
    public async Task<Publication?> FindAsync(Guid environmentId, long version, CancellationToken cancellationToken)
    {
        var row = await context.Publications
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.EnvironmentId == environmentId && p.Version == version, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyDictionary<Guid, Publication>> FindCurrentByEnvironmentAsync(
        Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await context.Publications
            .AsNoTracking()
            .Join(
                context.Environments.Where(e => e.ProjectId == projectId),
                publication => new { publication.EnvironmentId, Version = (long?)publication.Version },
                environment => new { EnvironmentId = environment.Id, Version = environment.CurrentVersion },
                (publication, _) => publication)
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.EnvironmentId, ToDomain);
    }

    public void Add(Publication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);

        context.Publications.Add(new PublicationRow
        {
            EnvironmentId = publication.EnvironmentId,
            Version = publication.Version,
            PublishedAt = publication.PublishedAt,
            PublishedBy = publication.PublishedBy,
            Origin = publication.Origin.ToString().ToLowerInvariant(),
            RestoredVersion = publication.RestoredVersion,
            Summary = publication.Summary?.Value,
            Flags = publication.Flags.Select(ToRow).ToList(),
        });
    }

    private static PublicationFlagRow ToRow(PublishedFlag flag) => new()
    {
        Key = flag.Key.Value,
        IsEnabled = flag.Settings.IsEnabled,
        OnValue = flag.Settings.OnValue,
        OffValue = flag.Settings.OffValue,
        RolloutBasisPoints = flag.Settings.Rollout.BasisPoints,
        Seed = flag.Seed.Value,
    };

    private static Publication ToDomain(PublicationRow row) =>
        Publication.Restore(
            row.EnvironmentId,
            row.Version,
            row.PublishedAt,
            row.PublishedBy,
            Enum.Parse<PublicationOrigin>(row.Origin, ignoreCase: true),
            row.RestoredVersion,
            row.Summary is null ? null : ValueConverters.ParseChangeSummary(row.Summary),
            row.Flags.OrderBy(flag => flag.Key, StringComparer.Ordinal).Select(ToDomain).ToList());

    private static PublishedFlag ToDomain(PublicationFlagRow row) =>
        new(
            FlagKey.Parse(row.Key),
            new FlagSettings(row.IsEnabled, row.OnValue, row.OffValue, RolloutPercentage.FromBasisPoints(row.RolloutBasisPoints)),
            RolloutSeed.Parse(row.Seed));
}
