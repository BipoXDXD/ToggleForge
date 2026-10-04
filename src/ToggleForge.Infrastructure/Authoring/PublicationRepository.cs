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

    /// <summary>Keyset pela PK (environment_id, version): cada página é uma busca no índice, sem OFFSET.</summary>
    public async Task<IReadOnlyList<PublicationHeader>> ListAsync(
        Guid environmentId, long? beforeVersion, int limit, CancellationToken cancellationToken)
    {
        var query = context.Publications.AsNoTracking().Where(p => p.EnvironmentId == environmentId);
        if (beforeVersion is { } before)
        {
            query = query.Where(p => p.Version < before);
        }

        var rows = await query
            .OrderByDescending(p => p.Version)
            .Take(limit)
            .Select(p => new { p.Version, p.PublishedAt, p.PublishedBy, p.Origin, p.RestoredVersion, p.Summary })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new PublicationHeader(
                row.Version,
                row.PublishedAt,
                row.PublishedBy,
                ValueConverters.ParsePublicationOrigin(row.Origin),
                row.RestoredVersion,
                SummaryOf(row.Summary)))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, Publication>> FindCurrentByEnvironmentAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        var rows = await context.Publications
            .AsNoTracking()
            .Join(
                context.Environments.Where(e => projectIds.Contains(e.ProjectId)),
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
        Type = flag.Settings.Type,
        OnValue = flag.Settings.Values.OnValue,
        OffValue = flag.Settings.Values.OffValue,
        RolloutBasisPoints = flag.Settings.Rollout.BasisPoints,
        Seed = flag.Seed.Value,
    };

    private static Publication ToDomain(PublicationRow row) =>
        Publication.Restore(
            row.EnvironmentId,
            row.Version,
            row.PublishedAt,
            row.PublishedBy,
            ValueConverters.ParsePublicationOrigin(row.Origin),
            row.RestoredVersion,
            SummaryOf(row.Summary),
            row.Flags.OrderBy(flag => flag.Key, StringComparer.Ordinal).Select(ToDomain).ToList());

    private static ChangeSummary? SummaryOf(string? summary) =>
        summary is null ? null : ValueConverters.ParseChangeSummary(summary);

    private static PublishedFlag ToDomain(PublicationFlagRow row) =>
        new(
            ValueConverters.ParseFlagKey(row.Key),
            new FlagSettings(
                row.IsEnabled,
                FlagValues.Restore(row.Type, row.OnValue, row.OffValue),
                ValueConverters.ParseRolloutPercentage(row.RolloutBasisPoints)),
            ValueConverters.ParseRolloutSeed(row.Seed));
}
