using System;

namespace ToggleForge.Sdk
{
    /// <summary>O que o SDK guarda entre execuções do jogo: o corpo como veio da API, a ETag e a última confirmação.</summary>
    public sealed class StoredSnapshot
    {
        /// <param name="confirmedAt">A última vez que o servidor confirmou este snapshot (200 ou 304), em UTC.</param>
        public StoredSnapshot(string body, string? eTag, DateTimeOffset confirmedAt)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            ETag = eTag;
            ConfirmedAt = confirmedAt;
        }

        public string Body { get; }

        public string? ETag { get; }

        public DateTimeOffset ConfirmedAt { get; }
    }
}
