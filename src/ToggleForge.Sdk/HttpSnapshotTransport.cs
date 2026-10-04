using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ToggleForge.Sdk
{
    /// <summary>O transporte do .NET, com um <see cref="HttpClient"/> que aponta para a API do ToggleForge.</summary>
    public sealed class HttpSnapshotTransport : ISnapshotTransport
    {
        // Um snapshot tem no máximo 200 flags de poucas centenas de bytes cada; o teto barra uma resposta que
        // não é snapshot (um proxy no caminho, por exemplo) antes de ela ocupar a memória do jogo.
        internal const int MaxSnapshotBytes = 1024 * 1024;

        private const string SnapshotPath = "sdk/v1/snapshot";

        private readonly HttpClient _http;
        private readonly SdkCredential _credential;
        private readonly TimeSpan _timeout;

        /// <param name="http">Com <c>BaseAddress</c> na raiz da API. O transporte não o descarta.</param>
        /// <param name="timeout">
        /// O tempo de uma tentativa inteira, do envio ao fim do corpo; 10 s quando nulo (ADR 0010). O
        /// <c>Timeout</c> do <see cref="HttpClient"/> não serve: ele para de contar quando chegam os headers.
        /// </param>
        public HttpSnapshotTransport(HttpClient http, SdkCredential credential, TimeSpan? timeout = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _credential = credential ?? throw new ArgumentNullException(nameof(credential));
            _timeout = timeout ?? TimeSpan.FromSeconds(10);
        }

        public async Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, SnapshotPath);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credential.Value);
            if (eTag is not null)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", eTag);
            }

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(_timeout);
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attempt.Token)
                    .ConfigureAwait(false);
                return await ReadAsync(response, attempt.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                // A conexão pode cair também no meio do corpo, depois dos headers.
                return SnapshotResponse.Failed(statusCode: null);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout: o cancelamento veio do prazo da tentativa, não de quem chamou.
                return SnapshotResponse.Failed(statusCode: null);
            }
        }

        private static async Task<SnapshotResponse> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return SnapshotResponse.NotModified;
            }

            if (!response.IsSuccessStatusCode)
            {
                return SnapshotResponse.Failed((int)response.StatusCode, response.Headers.RetryAfter?.Delta);
            }

            var body = await ReadLimitedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return body is null
                ? SnapshotResponse.Failed((int)response.StatusCode)
                : SnapshotResponse.Fetched(body, response.Headers.ETag?.ToString());
        }

        /// <returns>Nulo quando o corpo passa de <see cref="MaxSnapshotBytes"/>.</returns>
        private static async Task<string?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaxSnapshotBytes)
            {
                return null;
            }

            using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxSnapshotBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }
}
