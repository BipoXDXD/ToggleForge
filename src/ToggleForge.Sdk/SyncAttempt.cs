using System;
using System.Net;

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

        /// <summary>401 ou 403: a credencial foi revogada ou recusada, e não volta a valer sozinha em segundos.</summary>
        public bool IsCredentialRefused =>
            Outcome == SyncOutcome.Failed && StatusCode is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden;

        /// <summary>404: o ambiente ainda não tem nenhuma publicação.</summary>
        public bool IsNeverPublished => Outcome == SyncOutcome.Failed && StatusCode == (int)HttpStatusCode.NotFound;
    }
}
