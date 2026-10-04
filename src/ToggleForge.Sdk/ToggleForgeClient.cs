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
        // Gravar o snapshot a cada 304 seria uma escrita por minuto no disco do celular; a confirmação guardada
        // pode atrasar até isso, bem menos que a idade máxima.
        private static readonly TimeSpan _persistConfirmationEvery = TimeSpan.FromHours(1);

        private readonly ISnapshotTransport _transport;
        private readonly ISnapshotStore? _store;
        private readonly FlagEvaluator _evaluator = new();
        private readonly TimeSpan _maxCacheAge;
        private readonly SyncSchedule _schedule;
        private readonly TimeProvider _timeProvider;
        private readonly Func<TimeSpan, CancellationToken, Task> _wait;
        private readonly Func<double> _sample;
        private readonly CancellationTokenSource _stopping = new();
        // Nunca descartado: um RefreshAsync atrasado ainda pode passar por ele, e sem AvailableWaitHandle o
        // SemaphoreSlim não segura recurso nenhum.
        private readonly SemaphoreSlim _persisting = new(1, 1);

        // Guardado já na criação: depois de DisposeAsync, pedir o token à fonte descartada lançaria exceção.
        private readonly CancellationToken _stoppingToken;

        // Imutável e trocado inteiro: quem avalia vê o snapshot antigo ou o novo, nunca uma mistura.
        private SyncedSnapshot? _current;

        // O último estado gravado no armazenamento; só quem segura _persisting o lê ou escreve.
        private SyncedSnapshot? _persisted;

        private Task? _loop;
        private int _started;
        private int _disposed;

        // Instante monotônico (TimeProvider.GetTimestamp) antes do qual RefreshAsync não chama o servidor.
        private long _refreshNotBefore = long.MinValue;

        /// <param name="store">Onde o snapshot sobrevive a um reinício do jogo; nulo guarda só em memória.</param>
        /// <param name="timeProvider">O relógio do SDK; nulo usa o do sistema.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// O intervalo está fora de <see cref="ToggleForgeOptions.MinSyncInterval"/> a
        /// <see cref="ToggleForgeOptions.MaxSyncInterval"/>, ou a idade máxima do cache não é positiva.
        /// </exception>
        public ToggleForgeClient(
            ISnapshotTransport transport, ISnapshotStore? store = null, ToggleForgeOptions? options = null, TimeProvider? timeProvider = null)
            : this(transport, store, options, timeProvider ?? TimeProvider.System, wait: null, new Random().NextDouble)
        {
        }

        /// <param name="wait">A espera entre sincronizações; nula usa o <paramref name="timeProvider"/>.</param>
        /// <param name="sample">Números aleatórios em [0, 1) para a variação do intervalo.</param>
        internal ToggleForgeClient(
            ISnapshotTransport transport,
            ISnapshotStore? store,
            ToggleForgeOptions? options,
            TimeProvider timeProvider,
            Func<TimeSpan, CancellationToken, Task>? wait,
            Func<double> sample)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _store = store;
            options ??= new ToggleForgeOptions();
            if (options.MaxCacheAge <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options), options.MaxCacheAge, $"{nameof(ToggleForgeOptions.MaxCacheAge)} must be positive.");
            }

            _maxCacheAge = options.MaxCacheAge;
            var interval = options.SyncInterval;
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
            if (current is not null && IsExpired(current))
            {
                return FlagResult<bool>.Expired(defaultValue);
            }

            var evaluation = _evaluator.EvaluateBoolean(current?.Snapshot, flagKey, context, defaultValue);
            return new FlagResult<bool>(evaluation, current?.Source ?? SnapshotSource.None);
        }

        /// <summary>
        /// Adota o snapshot guardado, se houver, começa a sincronizar em segundo plano, no intervalo configurado,
        /// até o cliente ser descartado, e espera a primeira sincronização. O <paramref name="cancellationToken"/> limita só essa espera: o jogo pode
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
                await AdoptStoredAsync(stopping).ConfigureAwait(false);
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

        private bool IsExpired(SyncedSnapshot snapshot) => _timeProvider.GetUtcNow() - snapshot.ConfirmedAt > _maxCacheAge;

        /// <summary>
        /// O cache só entra se ainda não há snapshot: nunca passa por cima do que veio do servidor. Mesmo expirado,
        /// ele é adotado, porque a ETag dele ainda pode render um 304 que o confirma de novo.
        /// </summary>
        private async Task AdoptStoredAsync(CancellationToken cancellationToken)
        {
            if (_store is null)
            {
                return;
            }

            StoredSnapshot? stored;
            try
            {
                stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // Disco ilegível é o mesmo que nenhum cache: a primeira sincronização decide.
                return;
            }

            if (stored is null || SnapshotReader.Read(stored.Body) is not { } snapshot)
            {
                return;
            }

            var adopted = new SyncedSnapshot(snapshot, stored.Body, stored.ETag, SnapshotSource.Cache, stored.ConfirmedAt);
            if (Interlocked.CompareExchange(ref _current, adopted, null) is null)
            {
                _persisted = adopted;
            }
        }

        private async Task<SyncAttempt> SyncAsync(CancellationToken cancellationToken)
        {
            var attempt = await AttemptAsync(cancellationToken).ConfigureAwait(false);
            if (attempt.Outcome is SyncOutcome.Updated or SyncOutcome.NotModified)
            {
                await PersistAsync(cancellationToken).ConfigureAwait(false);
            }

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
                { Kind: SnapshotResponseKind.NotModified } => new SyncAttempt(Confirm(known)),
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

            var received = new SyncedSnapshot(snapshot, body, eTag, SnapshotSource.Server, _timeProvider.GetUtcNow());
            return TryAdvance(received) ? SyncOutcome.Updated : SyncOutcome.OutOfOrder;
        }

        /// <summary>
        /// O servidor disse que o snapshot cuja ETag foi enviada continua valendo: ele passa a contar como
        /// confirmado agora. Se outro snapshot entrou nesse meio-tempo, a confirmação não vale para ele.
        /// </summary>
        private SyncOutcome Confirm(SyncedSnapshot? asked)
        {
            if (asked is not null)
            {
                Interlocked.CompareExchange(ref _current, asked.ConfirmedAgainAt(_timeProvider.GetUtcNow()), asked);
            }

            return SyncOutcome.NotModified;
        }

        /// <summary>
        /// Grava o snapshot em uso. Uma gravação por vez, sempre do estado mais recente, então uma sincronização
        /// atrasada nunca grava por cima de uma mais nova. Falha de disco não chega ao jogo.
        /// </summary>
        private async Task PersistAsync(CancellationToken cancellationToken)
        {
            if (_store is null)
            {
                return;
            }

            await _persisting.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var current = Volatile.Read(ref _current);
                if (current is null || !NeedsPersisting(current))
                {
                    return;
                }

                await _store.SaveAsync(new StoredSnapshot(current.Body, current.ETag, current.ConfirmedAt), cancellationToken).ConfigureAwait(false);
                _persisted = current;
            }
            catch (Exception exception) when (!(exception is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // Sem disco, o jogo segue com o snapshot em memória; a próxima sincronização tenta gravar de novo.
            }
            finally
            {
                _persisting.Release();
            }
        }

        private bool NeedsPersisting(SyncedSnapshot current) =>
            _persisted is not { } persisted
            || !ReferenceEquals(persisted.Snapshot, current.Snapshot)
            || current.ConfirmedAt - persisted.ConfirmedAt >= _persistConfirmationEvery;

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
            public SyncedSnapshot(ConfigurationSnapshot snapshot, string body, string? eTag, SnapshotSource source, DateTimeOffset confirmedAt)
            {
                Snapshot = snapshot;
                Body = body;
                ETag = eTag;
                Source = source;
                ConfirmedAt = confirmedAt;
            }

            public ConfigurationSnapshot Snapshot { get; }

            /// <summary>O JSON como veio da API, que é o que vai para o armazenamento.</summary>
            public string Body { get; }

            public string? ETag { get; }

            public SnapshotSource Source { get; }

            /// <summary>A última confirmação do servidor; dela se conta a idade máxima.</summary>
            public DateTimeOffset ConfirmedAt { get; }

            public SyncedSnapshot ConfirmedAgainAt(DateTimeOffset instant) => new(Snapshot, Body, ETag, SnapshotSource.Server, instant);

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
