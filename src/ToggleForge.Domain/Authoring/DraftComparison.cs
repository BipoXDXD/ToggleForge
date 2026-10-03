namespace ToggleForge.Domain.Authoring;

public static class DraftComparison
{
    /// <summary>
    /// Diferenças entre o rascunho e a publicação atual, ordenadas pela chave. Uma flag removida e
    /// recriada com a mesma chave conta como modificada, porque a seed nova redistribui os jogadores.
    /// </summary>
    public static IReadOnlyList<DraftChange> Changes(
        IReadOnlyCollection<PublishedFlag> draft, IReadOnlyCollection<PublishedFlag> published)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(published);

        var publishedByKey = published.ToDictionary(flag => flag.Key);
        var draftKeys = draft.Select(flag => flag.Key).ToHashSet();

        var addedOrModified = draft
            .Where(flag => !publishedByKey.TryGetValue(flag.Key, out var current) || current != flag)
            .Select(flag => new DraftChange(
                flag.Key, publishedByKey.ContainsKey(flag.Key) ? DraftChangeKind.Modified : DraftChangeKind.Added));
        var removed = published
            .Where(flag => !draftKeys.Contains(flag.Key))
            .Select(flag => new DraftChange(flag.Key, DraftChangeKind.Removed));

        return addedOrModified.Concat(removed).OrderBy(change => change.FlagKey.Value, StringComparer.Ordinal).ToList();
    }
}
