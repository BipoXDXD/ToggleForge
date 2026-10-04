using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

/// <summary>
/// O tipo com que cada chave já foi publicada num ambiente. Uma chave publicada mantém o tipo em toda publicação
/// seguinte do ambiente, mesmo depois de apagada do rascunho: um build no ar lê a chave com a classe dele (ADR 0011).
/// Mudar o tipo exige uma chave nova.
/// </summary>
public sealed class PublishedFlagTypes
{
    private readonly Dictionary<FlagKey, FlagType> _types;

    /// <param name="types">Uma entrada por chave já publicada no ambiente.</param>
    public PublishedFlagTypes(IEnumerable<KeyValuePair<FlagKey, FlagType>> types) => _types = new(types);

    /// <summary>O ambiente que nunca publicou.</summary>
    public static PublishedFlagTypes None { get; } = new([]);

    public bool Contains(FlagKey key) => _types.ContainsKey(key);

    /// <summary>Nula quando <paramref name="key"/> nunca foi publicada ou já foi publicada com <paramref name="type"/>.</summary>
    internal Failure? Refuse(FlagKey key, FlagType type) =>
        _types.TryGetValue(key, out var published) && published != type ? AuthoringFailures.FlagTypeLocked(key, published) : null;

    /// <summary>A falha da primeira flag de <paramref name="content"/> com tipo diferente do publicado; nula se nenhuma.</summary>
    internal Failure? Refuse(IEnumerable<PublishedFlag> content) =>
        content.Select(flag => Refuse(flag.Key, flag.Settings.Type)).FirstOrDefault(failure => failure is not null);
}
