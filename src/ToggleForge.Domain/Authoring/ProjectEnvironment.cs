using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

/// <summary>
/// Ambiente de um projeto: dono do rascunho e de qual publicação está em vigor. Toda mudança no rascunho
/// incrementa <see cref="DraftRevision"/>, que serve de controle de concorrência para editar e publicar.
/// </summary>
public sealed class ProjectEnvironment
{
    public const int MaxFlags = 200;

    private const long InitialDraftRevision = 1;

    private readonly List<DraftFlag> _flags = [];

    private ProjectEnvironment(
        Guid id, Guid projectId, EnvironmentKey key, DisplayName name, long draftRevision, long? currentVersion)
    {
        Id = id;
        ProjectId = projectId;
        Key = key;
        Name = name;
        DraftRevision = draftRevision;
        CurrentVersion = currentVersion;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public EnvironmentKey Key { get; }

    public DisplayName Name { get; }

    public long DraftRevision { get; private set; }

    /// <summary>Nula até a primeira publicação.</summary>
    public long? CurrentVersion { get; private set; }

    public IReadOnlyList<DraftFlag> Flags => _flags;

    internal static ProjectEnvironment Create(Guid projectId, EnvironmentKey key, DisplayName name) =>
        new(Guid.NewGuid(), projectId, key, name, InitialDraftRevision, currentVersion: null);

    public Result<DraftFlag> AddFlag(FlagKey key, FlagSettings settings, RolloutSeed seed)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(seed);

        if (_flags.Any(flag => flag.Key == key))
        {
            return AuthoringFailures.FlagKeyTaken;
        }

        if (_flags.Count >= MaxFlags)
        {
            return AuthoringFailures.FlagLimitReached;
        }

        DraftRevision++;
        var flag = DraftFlag.Create(key, settings, seed, DraftRevision);
        _flags.Add(flag);
        return flag;
    }

    /// <summary>
    /// Muda os campos informados em <paramref name="change"/>, desde que a flag ainda esteja na revisão
    /// <paramref name="expectedRevision"/>. Uma mudança que não altera nada não gera revisão nova.
    /// </summary>
    public Result<DraftFlag> ChangeFlag(FlagKey key, long expectedRevision, FlagChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!FindFlag(key, expectedRevision).TryGetValue(out var flag, out var failure))
        {
            return failure;
        }

        var settings = change.ApplyTo(flag.Settings);
        if (settings == flag.Settings)
        {
            return flag;
        }

        DraftRevision++;
        flag.Change(settings, DraftRevision);
        return flag;
    }

    /// <summary>Tira a flag do rascunho; os jogos continuam com ela até a próxima publicação.</summary>
    public Result<DraftFlag> RemoveFlag(FlagKey key, long expectedRevision)
    {
        if (!FindFlag(key, expectedRevision).TryGetValue(out var flag, out var failure))
        {
            return failure;
        }

        _flags.Remove(flag);
        DraftRevision++;
        return flag;
    }

    /// <summary>Compara o rascunho com <paramref name="current"/>, que deve ser a publicação em vigor.</summary>
    public IReadOnlyList<DraftChange> PendingChanges(Publication? current)
    {
        EnsureIsCurrent(current);
        return DraftComparison.Changes(DraftContent(), current?.Flags ?? []);
    }

    public Result<Publication> Publish(
        long reviewedDraftRevision, ChangeSummary? summary, Guid publishedBy, DateTimeOffset publishedAt, Publication? current)
    {
        if (reviewedDraftRevision != DraftRevision)
        {
            return AuthoringFailures.DraftChanged;
        }

        if (PendingChanges(current).Count == 0)
        {
            return AuthoringFailures.NothingToPublish;
        }

        var publication = new Publication(
            Id,
            (CurrentVersion ?? 0) + 1,
            publishedAt,
            publishedBy,
            PublicationOrigin.Publication,
            restoredVersion: null,
            summary,
            DraftContent());
        CurrentVersion = publication.Version;
        return publication;
    }

    private Result<DraftFlag> FindFlag(FlagKey key, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_flags.Find(flag => flag.Key == key) is not { } flag)
        {
            return AuthoringFailures.FlagNotFound;
        }

        return flag.ChangedAtRevision == expectedRevision ? flag : AuthoringFailures.FlagChanged;
    }

    private List<PublishedFlag> DraftContent() =>
        _flags.OrderBy(flag => flag.Key.Value, StringComparer.Ordinal).Select(flag => flag.ToPublished()).ToList();

    private void EnsureIsCurrent(Publication? current)
    {
        if (current?.Version != CurrentVersion || (current is not null && current.EnvironmentId != Id))
        {
            throw new InvalidOperationException(
                $"Expected the publication in force (version {CurrentVersion}) of environment {Id}.");
        }
    }
}
