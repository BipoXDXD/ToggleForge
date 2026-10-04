using System.Globalization;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// Faz o papel de <c>GET /sdk/v1/snapshot</c>, com o mesmo JSON e a mesma regra de ETag da API, e deixa o roteiro
/// derrubar a rede ou entregar uma resposta atrasada.
/// </summary>
internal sealed class SimulatedServer : ISnapshotTransport
{
    private const string Seed = "seed-demo";

    private readonly List<string> _publications = [];
    private int? _staleVersion;

    public bool IsOnline { get; set; }

    public long CurrentVersion => _publications.Count;

    public void Publish(int rolloutBasisPoints) =>
        _publications.Add(Snapshot(_publications.Count + 1, rolloutBasisPoints));

    /// <summary>A próxima resposta traz uma versão antiga, como um pacote que se atrasou na rede.</summary>
    public void DeliverLateOnce(int version) => _staleVersion = version;

    public Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
    {
        if (!IsOnline)
        {
            return Task.FromResult(SnapshotResponse.Failed(statusCode: null));
        }

        if (_staleVersion is { } stale)
        {
            _staleVersion = null;
            return Task.FromResult(SnapshotResponse.Fetched(_publications[stale - 1], ETagOf(stale)));
        }

        if (_publications.Count == 0)
        {
            return Task.FromResult(SnapshotResponse.Failed(404));
        }

        var current = ETagOf(_publications.Count);
        return Task.FromResult(eTag == current ? SnapshotResponse.NotModified : SnapshotResponse.Fetched(_publications[^1], current));
    }

    private static string ETagOf(long version) => string.Create(CultureInfo.InvariantCulture, $"\"v{version}\"");

    private static string Snapshot(long version, int rolloutBasisPoints) => string.Create(
        CultureInfo.InvariantCulture,
        $$"""
        {"algorithm":"toggleforge.rollout.v1","projectKey":"space-race","environmentKey":"prod","version":{{version}},
         "publishedAt":"2026-10-04T12:00:00Z",
         "flags":[{"key":"new-shop","enabled":true,"onValue":true,"offValue":false,"rolloutBasisPoints":{{rolloutBasisPoints}},"seed":"{{Seed}}"}]}
        """);
}
