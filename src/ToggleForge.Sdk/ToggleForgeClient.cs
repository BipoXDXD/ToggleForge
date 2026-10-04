using System;
using System.Threading;
using System.Threading.Tasks;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// O SDK dentro do jogo: sincroniza o snapshot do ambiente da credencial e avalia as flags localmente. A
    /// avaliação lê o snapshot em uso sem lock, rede nem disco, e nunca lança exceção por configuração ruim.
    /// <see cref="StartAsync"/> mantém o snapshot em dia em segundo plano até o cliente ser descartado.
    /// </summary>
    public sealed class ToggleForgeClient : IAsyncDisposable, IDisposable
    {
        private readonly ISnapshotTransport _transport;
        private readonly FlagEvaluator _evaluator = new();
        private readonly SyncSchedule _schedule;
        private readonly TimeProvider _timeProvider;
        private readonly Func<TimeSpan, CancellationToken, Task> _wait;
        private readonly Func<double> _sample;
        private readonly CancellationTokenSource _stopping = new();

        // Guardado já na criação: depois de DisposeAsync, pedir o token à fonte descartada lançaria exceção.
        private readonly CancellationToken _stoppingToken;

        // Imutável e trocado inteiro: quem avalia vê o snapshot antigo ou o novo, nunca uma mistura.
        private SyncedSnapshot? _current;

        private Task? _loop;
        private int _started;
        private int _disposed;

        // Instante monotônico (TimeProvider.GetTimestamp) antes do qual RefreshAsync não chama o servidor.
        private long _refreshNotBefore = long.MinValue;

        /// <exception cref="ArgumentOutOfRangeException">
        /// O intervalo está fora de <see cref="ToggleForgeOptions.MinSyncInterval"/> a
        /// <see cref="ToggleForgeOptions.MaxSyncInterval"/>.
        /// </exception>
        /// <param name="timeProvider">O relógio do SDK; nulo usa o do sistema.</param>
        public ToggleForgeClient(ISnapshotTransport transport, ToggleForgeOptions? options = null, TimeProvider? timeProvider = null)
            : this(transport, options, timeProvider ?? TimeProvider.System, wait: null, new Random().NextDouble)
        {
        }

        /// <param name="wait">A espera entre sincronizações; nula usa o <paramref name="timeProvider"/>.</param>
        /// <param name="sample">Números aleatórios em [0, 1) para a variação do intervalo.</param>
        internal ToggleForgeClient(
            ISnapshotTransport transport,
            ToggleForgeOptions? options,
            TimeProvider timeProvider,
            Func<TimeSpan, CancellationToken, Task>? wait,
            Func<double> sample)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            var interval = (options ?? new ToggleForgeOptions()).SyncInterval;
            if (interval < ToggleForgeOptions.MinSyncInterval || interval > ToggleForgeOptions.MaxSyncInterval)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    interval,
                    $"{nameof(ToggleForgeOptions.SyncInterval)} must be between {ToggleForgeOptions.MinSyncInterval} and {ToggleForgeOptions.MaxSyncInterval}.");
            }

            _schedule = new SyncSchedule(interval);
            _timeProvider = timeProvider;
            _wait = wait ?? ((delay, cancellationToken) => timeProvider.Delay(delay, cancellationToken));
            _sample = sample;
            _stoppingToken = _stopping.Token;
        }

        public FlagResult<bool> EvaluateBoolean(string flagKey, EvaluationContext context, bool defaultValue)
        {
            var current = Volatile.Read(ref _current);
            var evaluation = _evaluator.EvaluateBoolean(current?.Snapshot, flagKey, context, defaultValue);
            return new FlagResult<bool>(evaluation, current is null ? SnapshotSource.None : SnapshotSource.Server);
        }

        /// <summary>
        /// Começa a sincronizar em segundo plano, no intervalo configurado, até o cliente ser descartado, e espera
        /// a primeira sincronização. O <paramref name="cancellationToken"/> limita só essa espera: o jogo pode
        /// desistir dela, por exemplo ao fim da tela de abertura, e seguir com os valores padrão enquanto o SDK
        /// continua tentando.
        /// </summary>
        /// <exception cref="InvalidOperationException">O cliente já foi iniciado.</exception>
        /// <exception cref="ObjectDisposedException">O cliente já foi descartado.</exception>
        /// <exception cref="OperationCanceledException">O jogo desistiu de esperar; a sincronização continua.</exception>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                throw new InvalidOperationException("The client has already been started.");
            }

            var firstSync = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _loop, KeepSyncingAsync(firstSync, _stoppingToken));
            await WaitUnlessGivenUpAsync(firstSync.Task, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Uma sincronização agora, por exemplo quando o jogo volta do segundo plano. Falha de rede ou do servidor
        /// não lança exceção: o snapshot em uso continua valendo, e o resultado diz o que aconteceu. Chamada cedo
        /// demais, antes de 15 s da última tentativa ou do prazo que o servidor pediu, ela nem sai e devolve
        /// <see cref="SyncOutcome.Skipped"/>: o jogo não consegue furar o ritmo que protege a API.
        /// </summary>
        public async Task<SyncOutcome> RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (_timeProvider.GetTimestamp() < Volatile.Read(ref _refreshNotBefore))
            {
                return SyncOutcome.Skipped;
            }

            return (await SyncAsync(cancellationToken).ConfigureAwait(false)).Outcome;
        }

        /// <summary>Para o laço, inclusive uma sincronização em andamento, e espera ele terminar.</summary>
        /// <remarks>A avaliação continua com o último snapshot.</remarks>
        public async ValueTask DisposeAsync()
        {
            Stop();
            try
            {
                if (Volatile.Read(ref _loop) is { } loop)
                {
                    await loop.ConfigureAwait(false);
                }
            }
            finally
            {
                _stopping.Dispose();
            }
        }

        /// <summary>Para o laço sem esperar, para quem não pode aguardar, como o <c>OnDestroy</c> da Unity.</summary>
        public void Dispose() => Stop();

        private static async Task WaitUnlessGivenUpAsync(Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var givenUp = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(() => givenUp.TrySetCanceled(cancellationToken)))
            {
                var first = await Task.WhenAny(task, givenUp.Task).ConfigureAwait(false);
                await first.ConfigureAwait(false);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                throw new ObjectDisposedException(nameof(ToggleForgeClient));
            }
        }

        private void Stop()
        {
            Interlocked.Exchange(ref _disposed, 1);
            try
            {
                _stopping.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Já descartado por DisposeAsync: nada mais para parar.
            }
        }

        /// <param name="firstSync">Termina depois da primeira tentativa, ou quando o laço é parado antes dela.</param>
        private async Task KeepSyncingAsync(TaskCompletionSource<bool> firstSync, CancellationToken stopping)
        {
            try
            {
                var last = await SyncAsync(stopping).ConfigureAwait(false);
                firstSync.TrySetResult(true);
                var failures = SyncSchedule.CountsAsFailure(last) ? 1 : 0;
                while (true)
                {
                    await _wait(_schedule.DelayAfter(last, failures, _sample()), stopping).ConfigureAwait(false);
                    last = await SyncAsync(stopping).ConfigureAwait(false);
                    failures = SyncSchedule.CountsAsFailure(last) ? failures + 1 : 0;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                // Descartado: o laço termina aqui.
            }
            finally
            {
                firstSync.TrySetResult(true);
            }
        }

        private async Task<SyncAttempt> SyncAsync(CancellationToken cancellationToken)
        {
            var attempt = await AttemptAsync(cancellationToken).ConfigureAwait(false);
            var pause = SyncSchedule.PauseBeforeRefresh(attempt);
            Volatile.Write(ref _refreshNotBefore, _timeProvider.GetTimestamp() + (long)(pause.TotalSeconds * _timeProvider.TimestampFrequency));
            return attempt;
        }

        private async Task<SyncAttempt> AttemptAsync(CancellationToken cancellationToken)
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
                return new SyncAttempt(SyncOutcome.Failed);
            }

            return response switch
            {
                { Kind: SnapshotResponseKind.NotModified } => new SyncAttempt(SyncOutcome.NotModified),
                { Kind: SnapshotResponseKind.Fetched, Body: { } body } => new SyncAttempt(Accept(body, response.ETag)),
                _ => new SyncAttempt(SyncOutcome.Failed, response.StatusCode, response.RetryAfter),
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
