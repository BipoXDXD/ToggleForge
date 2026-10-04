using System;

namespace ToggleForge.Sdk
{
    /// <summary>
    /// Quanto o laço espera depois de cada sincronização (ADR 0010): o intervalo quando deu certo, backoff
    /// exponencial com teto quando falhou, sempre com ±10% de variação para os jogadores não sincronizarem juntos.
    /// </summary>
    internal sealed class SyncSchedule
    {
        internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

        // O teto que o contrato dá ao Retry-After; acima disso, é um intermediário silenciando o jogo.
        internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

        private const double Jitter = 0.1;

        private readonly TimeSpan _interval;

        public SyncSchedule(TimeSpan interval) => _interval = interval;

        /// <summary>
        /// 404 é ambiente nunca publicado, não falha: a primeira publicação deve chegar no ritmo normal. Já um
        /// snapshot ilegível é falha, porque tentar de novo logo não o torna legível.
        /// </summary>
        public static bool CountsAsFailure(SyncAttempt attempt) =>
            attempt.Outcome == SyncOutcome.Invalid
            || (attempt.Outcome == SyncOutcome.Failed && !attempt.IsNeverPublished);

        /// <param name="consecutiveFailures">Quantas falhas seguidas, contando a última; 0 depois de um sucesso.</param>
        /// <param name="sample">Um número aleatório em [0, 1) que escolhe a variação.</param>
        public TimeSpan DelayAfter(SyncAttempt attempt, int consecutiveFailures, double sample)
        {
            var delay = Jittered(BaseDelay(attempt, consecutiveFailures), sample);
            if (attempt.RetryAfter is not { } asked)
            {
                return delay;
            }

            // A variação do Retry-After é só para cima: nunca antes do que o servidor pediu, e os jogadores que
            // levaram o mesmo 429 não voltam todos no mesmo segundo.
            var spreadRetry = TimeSpan.FromTicks((long)(Capped(asked).Ticks * (1 + (Jitter * sample))));
            return spreadRetry > delay ? spreadRetry : delay;
        }

        /// <summary>
        /// Quanto um <c>RefreshAsync</c> pedido pelo jogo espera depois de uma tentativa: o piso do intervalo, ou o
        /// prazo que o servidor deu, para o jogo não furar o ritmo que protege a API. Depois de uma credencial
        /// recusada, o mesmo teto do laço.
        /// </summary>
        public TimeSpan PauseBeforeRefresh(SyncAttempt attempt)
        {
            if (attempt.IsCredentialRefused)
            {
                return Ceiling;
            }

            return attempt.RetryAfter is { } asked && Capped(asked) > ToggleForgeOptions.MinSyncInterval
                ? Capped(asked)
                : ToggleForgeOptions.MinSyncInterval;
        }

        private TimeSpan BaseDelay(SyncAttempt attempt, int consecutiveFailures)
        {
            if (attempt.IsCredentialRefused)
            {
                return Ceiling;
            }

            if (!CountsAsFailure(attempt))
            {
                return _interval;
            }

            var doubled = _interval.Ticks * Math.Pow(2, Math.Max(consecutiveFailures, 1) - 1);
            return doubled >= Ceiling.Ticks ? Ceiling : TimeSpan.FromTicks((long)doubled);
        }

        // Um intervalo maior que o backoff máximo nunca é encurtado por uma falha.
        private TimeSpan Ceiling => _interval > MaxBackoff ? _interval : MaxBackoff;

        private static TimeSpan Jittered(TimeSpan delay, double sample) =>
            TimeSpan.FromTicks((long)(delay.Ticks * (1 - Jitter + (2 * Jitter * sample))));

        private static TimeSpan Capped(TimeSpan retryAfter) => retryAfter > MaxRetryAfter ? MaxRetryAfter : retryAfter;
    }
}
