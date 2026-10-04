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
        // Gravar o snapshot a cada 304 seria uma escrita por minuto no disco do celular. A confirmação guardada
        // pode atrasar até esse ritmo, que encolhe com idades máximas curtas para nunca passar de um quarto delas.
        private static readonly TimeSpan _slowestPersistRhythm = TimeSpan.FromHours(1);

        // Uma confirmação no futuro só existe se o relógio voltou depois dela; aceitá-la esticaria a idade máxima.
        // A folga cobre os acertos pequenos que o próprio sistema faz no relógio.
        private static readonly TimeSpan _clockTolerance = TimeSpan.FromMinutes(5);

        private readonly ISnapshotTransport _transport;
        private readonly ISnapshotStore? _store;
        private readonly FlagEvaluator _evaluator = new();
        private readonly TimeSpan _maxCacheAge;
        private readonly TimeSpan _persistConfirmationEvery;
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

        // O último estado gravado no armazenamento e quando, no relógio monotônico, esta execução o gravou (nulo
        // se veio do disco); só quem segura _persisting os lê ou escreve.
        private SyncedSnapshot? _persisted;
        private long? _persistedAt;

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
            var quarterOfMaxAge = TimeSpan.FromTicks(options.MaxCacheAge.Ticks / 4);
            _persistConfirmationEvery = quarterOfMaxAge < _slowestPersistRhythm ? quarterOfMaxAge : _slowestPersistRhythm;
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
        /// Avalia uma flag JSON (ADR 0011) e entrega o valor convertido pelo parser do jogo, que pode usar
        /// JsonUtility, Newtonsoft ou o que preferir. Como <see cref="EvaluateBoolean"/>, nunca lança exceção por
        /// configuração ruim: flag ausente, de outro tipo, snapshot expirado ou valor que o parser recusa devolvem
        /// <paramref name="defaultValue"/> com o motivo.
        /// </summary>
        /// <param name="parse">
        /// Converte o texto JSON compacto do valor escolhido. Roda a cada chamada: guarde o resultado em vez de avaliar a
        /// cada frame. Não é chamado quando o resultado é o valor padrão por falta de snapshot, snapshot expirado,
        /// chave inválida, flag ausente ou de outro tipo, ou jogador sem ID num rollout parcial. Se lançar exceção,
        /// volta <paramref name="defaultValue"/> com <see cref="EvaluationReason.InvalidValue"/> e a exceção se perde:
        /// para diagnosticá-la, registre-a dentro do próprio parser.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="parse"/> é nulo.</exception>
        public FlagResult<T> EvaluateJson<T>(string flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse)
        {
            if (parse is null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            var current = Volatile.Read(ref _current);
            if (current is not null && IsExpired(current))
            {
                return FlagResult<T>.Expired(defaultValue);
            }

            var evaluation = _evaluator.EvaluateJson(current?.Snapshot, flagKey, context, defaultValue, parse);
            return new FlagResult<T>(evaluation, current?.Source ?? SnapshotSource.None);
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

        private bool IsExpired(SyncedSnapshot snapshot)
        {
            var age = _timeProvider.GetUtcNow() - snapshot.ConfirmedAt;
            return age > _maxCacheAge || age < -_clockTolerance;
        }

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
            await _persisting.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Interlocked.CompareExchange(ref _current, adopted, null) is null)
                {
                    (_persisted, _persistedAt) = (adopted, null);
                }
            }
            finally
            {
                _persisting.Release();
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

        /// <summary>
        /// Só um snapshot mais novo entra, mesmo com sincronizações simultâneas: se outra trocou o estado entre a
        /// comparação e a troca, compara de novo com o que ela deixou. Um 200 com a versão em uso vale como
        /// confirmação, como um 304: acontece quando um proxy tira a ETag do caminho.
        /// </summary>
        private SyncOutcome Accept(string body, string? eTag)
        {
            if (SnapshotReader.Read(body) is not { } snapshot)
            {
                return SyncOutcome.Invalid;
            }

            var now = _timeProvider.GetUtcNow();
            var received = new SyncedSnapshot(snapshot, body, eTag, SnapshotSource.Server, now);
            while (true)
            {
                var current = Volatile.Read(ref _current);
                var (replacement, outcome) = current switch
                {
                    null => (received, SyncOutcome.Updated),
                    _ when received.IsSameVersionAs(current) => (current.ConfirmedAgainAt(now, eTag ?? current.ETag), SyncOutcome.NotModified),
                    _ when received.Supersedes(current) => (received, SyncOutcome.Updated),
                    _ => (current, SyncOutcome.OutOfOrder),
                };
                if (ReferenceEquals(replacement, current)
                    || Interlocked.CompareExchange(ref _current, replacement, current) == current)
                {
                    return outcome;
                }
            }
        }

        /// <summary>
        /// O servidor disse que o snapshot cuja ETag foi enviada continua valendo: ele passa a contar como
        /// confirmado agora. Se outro snapshot entrou nesse meio-tempo, a confirmação não vale para ele.
        /// </summary>
        private SyncOutcome Confirm(SyncedSnapshot? asked)
        {
            if (asked is not null)
            {
                Interlocked.CompareExchange(ref _current, asked.ConfirmedAgainAt(_timeProvider.GetUtcNow(), asked.ETag), asked);
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
                (_persisted, _persistedAt) = (current, _timeProvider.GetTimestamp());
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

        /// <summary>
        /// Versão ou ETag nova vai já para o disco. Uma confirmação nova vai no ritmo de gravação, medido no relógio
        /// monotônico: com o de parede, um relógio que voltou no tempo nunca mais deixaria gravar.
        /// </summary>
        private bool NeedsPersisting(SyncedSnapshot current)
        {
            if (_persisted is not { } persisted
                || !ReferenceEquals(persisted.Snapshot, current.Snapshot)
                || persisted.ETag != current.ETag)
            {
                return true;
            }

            return current.ConfirmedAt != persisted.ConfirmedAt
                && (_persistedAt is not { } savedAt || _timeProvider.GetElapsedTime(savedAt) >= _persistConfirmationEvery);
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

            public SyncedSnapshot ConfirmedAgainAt(DateTimeOffset instant, string? eTag) => new(Snapshot, Body, eTag, SnapshotSource.Server, instant);

            public bool IsSameVersionAs(SyncedSnapshot other) =>
                Snapshot.ProjectKey == other.Snapshot.ProjectKey
                && Snapshot.EnvironmentKey == other.Snapshot.EnvironmentKey
                && Snapshot.Version == other.Snapshot.Version;

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
