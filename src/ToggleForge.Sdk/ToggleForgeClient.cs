using System;
using System.Threading;
using System.Threading.Tasks;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// O SDK dentro do jogo: sincroniza o snapshot do ambiente da credencial e avalia as flags localmente. A
    /// avaliação lê o snapshot em uso sem lock, rede nem disco, e nunca lança exceção por configuração ruim.
    /// </summary>
    public sealed class ToggleForgeClient
    {
        private readonly ISnapshotTransport _transport;
        private readonly FlagEvaluator _evaluator = new();

        // Imutável e trocado inteiro: quem avalia vê o snapshot antigo ou o novo, nunca uma mistura.
        private SyncedSnapshot? _current;

        public ToggleForgeClient(ISnapshotTransport transport) =>
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        public FlagResult<bool> EvaluateBoolean(string flagKey, EvaluationContext context, bool defaultValue)
        {
            var current = Volatile.Read(ref _current);
            var evaluation = _evaluator.EvaluateBoolean(current?.Snapshot, flagKey, context, defaultValue);
            return new FlagResult<bool>(evaluation, current is null ? SnapshotSource.None : SnapshotSource.Server);
        }

        /// <summary>
        /// Uma tentativa de sincronização. Falha de rede ou do servidor não lança exceção: o snapshot em uso
        /// continua valendo, e o resultado diz o que aconteceu.
        /// </summary>
        public async Task<SyncOutcome> RefreshAsync(CancellationToken cancellationToken = default)
        {
            var known = Volatile.Read(ref _current);
            SnapshotResponse response;
            try
            {
                response = await _transport.GetAsync(known?.ETag, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // O transporte é uma porta pública, e um adaptador pode furar o contrato: falha dele não chega ao jogo.
                return SyncOutcome.Failed;
            }

            return response switch
            {
                { Kind: SnapshotResponseKind.NotModified } => SyncOutcome.NotModified,
                { Kind: SnapshotResponseKind.Fetched, Body: { } body } => Accept(body, response.ETag),
                _ => SyncOutcome.Failed,
            };
        }

        private SyncOutcome Accept(string body, string? eTag)
        {
            if (SnapshotReader.Read(body) is not { } snapshot)
            {
                return SyncOutcome.Invalid;
            }

            return TryAdvance(new SyncedSnapshot(snapshot, eTag)) ? SyncOutcome.Updated : SyncOutcome.OutOfOrder;
        }

        /// <summary>
        /// Só um snapshot mais novo entra, mesmo com sincronizações simultâneas: se outra trocou o estado entre a
        /// comparação e a troca, compara de novo com o que ela deixou.
        /// </summary>
        private bool TryAdvance(SyncedSnapshot candidate)
        {
            while (true)
            {
                var current = Volatile.Read(ref _current);
                if (current is not null && !candidate.Supersedes(current))
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _current, candidate, current) == current)
                {
                    return true;
                }
            }
        }

        private sealed class SyncedSnapshot
        {
            public SyncedSnapshot(ConfigurationSnapshot snapshot, string? eTag)
            {
                Snapshot = snapshot;
                ETag = eTag;
            }

            public ConfigurationSnapshot Snapshot { get; }

            public string? ETag { get; }

            /// <summary>
            /// A ordem vem da versão da publicação, nunca do relógio. Snapshot de outro projeto ou ambiente
            /// substitui o atual: quem escolhe o ambiente é a credencial, e versões de ambientes diferentes não se
            /// comparam.
            /// </summary>
            public bool Supersedes(SyncedSnapshot current) =>
                Snapshot.ProjectKey != current.Snapshot.ProjectKey
                || Snapshot.EnvironmentKey != current.Snapshot.EnvironmentKey
                || Snapshot.Version > current.Snapshot.Version;
        }
    }
}
