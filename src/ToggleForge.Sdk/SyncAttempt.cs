using System;

namespace ToggleForge.Sdk
{
    /// <summary>Uma sincronização com o que o laço precisa para decidir quando tentar de novo.</summary>
    internal sealed class SyncAttempt
    {
        public SyncAttempt(SyncOutcome outcome, int? statusCode = null, TimeSpan? retryAfter = null)
        {
            Outcome = outcome;
            StatusCode = statusCode;
            RetryAfter = retryAfter;
        }

        public SyncOutcome Outcome { get; }

        /// <summary>O status HTTP de uma falha; nulo quando a resposta nem chegou.</summary>
        public int? StatusCode { get; }

        public TimeSpan? RetryAfter { get; }
    }
}
