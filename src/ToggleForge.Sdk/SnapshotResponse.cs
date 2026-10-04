using System;

namespace ToggleForge.Sdk
{
    public enum SnapshotResponseKind
    {
        /// <summary>O servidor mandou um snapshot, que ainda será validado.</summary>
        Fetched,

        /// <summary>O snapshot atual continua valendo (HTTP 304).</summary>
        NotModified,

        /// <summary>Rede, timeout ou resposta de erro: o snapshot atual continua em uso.</summary>
        Failed,
    }

    /// <summary>O que um <see cref="ISnapshotTransport"/> conseguiu do servidor numa tentativa.</summary>
    public sealed class SnapshotResponse
    {
        private SnapshotResponse(SnapshotResponseKind kind, string? body, string? eTag, int? statusCode, TimeSpan? retryAfter)
        {
            Kind = kind;
            Body = body;
            ETag = eTag;
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }

        public static SnapshotResponse NotModified { get; } = new(SnapshotResponseKind.NotModified, null, null, null, null);

        public SnapshotResponseKind Kind { get; }

        /// <summary>O JSON recebido; só existe em <see cref="SnapshotResponseKind.Fetched"/>.</summary>
        public string? Body { get; }

        public string? ETag { get; }

        /// <summary>O status HTTP de uma falha; nulo quando a resposta nem chegou (rede ou timeout).</summary>
        public int? StatusCode { get; }

        /// <summary>Quanto o servidor pediu para esperar antes da próxima tentativa (HTTP 429).</summary>
        public TimeSpan? RetryAfter { get; }

        public static SnapshotResponse Fetched(string body, string? eTag) =>
            new(SnapshotResponseKind.Fetched, body ?? throw new ArgumentNullException(nameof(body)), eTag, null, null);

        /// <param name="statusCode">Nulo quando a resposta não chegou.</param>
        public static SnapshotResponse Failed(int? statusCode, TimeSpan? retryAfter = null) =>
            new(SnapshotResponseKind.Failed, null, null, statusCode, retryAfter);
    }
}
