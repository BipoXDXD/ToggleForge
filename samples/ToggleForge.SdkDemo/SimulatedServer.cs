using System.Globalization;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// Faz o papel de <c>GET /sdk/v1/snapshot</c>, com o mesmo JSON, o mesmo formato de ETag (a versão entre aspas) e a
/// mesma comparação fraca do <c>If-None-Match</c> da API, e deixa o roteiro derrubar a rede ou entregar uma
/// resposta atrasada.
/// </summary>
/// <param name="flagKey">A flag booleana publicada em toda versão, ligada e com valor <c>true</c>.</param>
/// <param name="seed">A semente do rollout dela.</param>
internal sealed class SimulatedServer(string flagKey, string seed) : ISnapshotTransport
{
    private const string WeakPrefix = "W/";

    private readonly List<string> _publications = [];
    private int? _staleVersion;

    public bool IsOnline { get; set; }

    /// <summary>O status da última resposta; nulo se a rede estava fora.</summary>
    public int? LastStatusCode { get; private set; }

    /// <param name="jsonFlag">Uma flag JSON ligada para todos, publicada junto; nula publica só a booleana.</param>
    public void Publish(int rolloutBasisPoints, (string Key, string Value)? jsonFlag = null) =>
        _publications.Add(Snapshot(_publications.Count + 1, rolloutBasisPoints, jsonFlag));

    /// <summary>A próxima resposta traz uma versão antiga, como um pacote que se atrasou na rede.</summary>
    public void DeliverLateOnce(int version) => _staleVersion = version;

    public Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken) => Task.FromResult(Respond(eTag));

    private SnapshotResponse Respond(string? eTag)
    {
        if (!IsOnline)
        {
            LastStatusCode = null;
            return SnapshotResponse.Failed(statusCode: null);
        }

        if (_staleVersion is { } stale)
        {
            _staleVersion = null;
            LastStatusCode = 200;
            return SnapshotResponse.Fetched(_publications[stale - 1], ETagOf(stale));
        }

        if (_publications.Count == 0)
        {
            LastStatusCode = 404;
            return SnapshotResponse.Failed(404);
        }

        var current = ETagOf(_publications.Count);
        if (eTag is not null && WithoutWeakPrefix(eTag) == current)
        {
            LastStatusCode = 304;
            return SnapshotResponse.NotModified;
        }

        LastStatusCode = 200;
        return SnapshotResponse.Fetched(_publications[^1], current);
    }

    private static string ETagOf(long version) => string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    private static string WithoutWeakPrefix(string eTag) => eTag.StartsWith(WeakPrefix, StringComparison.Ordinal) ? eTag[WeakPrefix.Length..] : eTag;

    private string Snapshot(long version, int rolloutBasisPoints, (string Key, string Value)? jsonFlag)
    {
        var flags = string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"key":"{{flagKey}}","type":"boolean","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":{{rolloutBasisPoints}},"seed":"{{seed}}"}""");
        if (jsonFlag is (var key, var value))
        {
            // O valor desligado repete o ligado, como o console sugere ao criar a flag (ADR 0011).
            flags += $$""",{"key":"{{key}}","type":"json","enabled":true,"onValue":{{value}},"offValue":{{value}},"rolloutBasisPoints":10000,"seed":"json-seed"}""";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $$"""
            {"algorithm":"toggleforge.rollout.v1","projectKey":"space-race","environmentKey":"prod","version":{{version}},
             "publishedAt":"2026-10-04T12:00:00Z","flags":[{{flags}}]}
            """);
    }
}
