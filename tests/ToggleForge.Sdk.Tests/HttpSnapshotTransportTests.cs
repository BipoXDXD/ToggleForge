using System.Net;
using System.Text;

namespace ToggleForge.Sdk.Tests;

public sealed class HttpSnapshotTransportTests
{
    [Fact]
    public async Task The_request_carries_the_credential_and_the_known_etag()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        await Transport(handler).GetAsync("\"v3\"", TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.toggleforge.test/sdk/v1/snapshot", request.RequestUri?.ToString());
        Assert.Equal($"Bearer {Snapshots.Credential}", request.Headers.Authorization?.ToString());
        Assert.Equal(["\"v3\""], request.Headers.GetValues("If-None-Match"));
    }

    [Fact]
    public async Task Without_an_etag_the_request_asks_for_the_whole_snapshot()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.False(Assert.Single(handler.Requests).Headers.Contains("If-None-Match"));
    }

    [Fact]
    public async Task A_snapshot_comes_with_its_etag()
    {
        var handler = new StubHandler(_ => Ok(Snapshots.Json(), "\"v1\""));

        var response = await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Fetched, Snapshots.Json(), "\"v1\""), (response.Kind, response.Body, response.ETag));
    }

    [Fact]
    public async Task Not_modified_keeps_the_snapshot_in_use()
    {
        var response = await Transport(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified))).GetAsync("\"v1\"", TestContext.Current.CancellationToken);

        Assert.Equal(SnapshotResponseKind.NotModified, response.Kind);
    }

    [Fact]
    public async Task Too_many_requests_carries_how_long_to_wait()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
            return response;
        });

        var response = await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)429, (TimeSpan?)TimeSpan.FromSeconds(42)), (response.Kind, response.StatusCode, response.RetryAfter));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task An_error_status_is_a_failure_with_the_status(HttpStatusCode status)
    {
        var response = await Transport(new StubHandler(_ => new HttpResponseMessage(status))).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)status), (response.Kind, response.StatusCode));
    }

    [Fact]
    public async Task A_network_failure_is_a_failure_without_status()
    {
        var response = await Transport(new StubHandler(_ => throw new HttpRequestException("down"))).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)null), (response.Kind, response.StatusCode));
    }

    [Fact]
    public async Task A_timeout_of_the_http_client_is_a_failure_without_status()
    {
        var response = await Transport(new StubHandler(_ => throw new TaskCanceledException("timeout"))).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)null), (response.Kind, response.StatusCode));
    }

    [Fact]
    public async Task Cancelling_the_sync_stops_it()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Transport(new StubHandler(_ => Ok(Snapshots.Json(), "\"v1\""))).GetAsync(eTag: null, stop.Token));
    }

    [Fact]
    public async Task A_body_declared_over_the_limit_is_refused_without_reading_it()
    {
        var body = new CountingStream(Encoding.UTF8.GetBytes(Snapshots.Json()));
        var handler = new StubHandler(_ =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = HttpSnapshotTransport.MaxSnapshotBytes + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });

        var response = await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)200, 0L), (response.Kind, response.StatusCode, body.BytesRead));
    }

    [Fact]
    public async Task A_body_without_length_is_read_only_up_to_the_limit()
    {
        var size = 4 * HttpSnapshotTransport.MaxSnapshotBytes;
        var body = new CountingStream(Encoding.UTF8.GetBytes(new string(' ', size)));

        var response = await Transport(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }))
            .GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal(SnapshotResponseKind.Failed, response.Kind);
        Assert.InRange(body.BytesRead, HttpSnapshotTransport.MaxSnapshotBytes, size / 2);
    }

    [Fact]
    public async Task Cancelling_while_the_body_arrives_stops_the_sync()
    {
        using var stop = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            // Os headers chegaram; o jogo desiste antes do corpo.
            stop.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Snapshots.Json()))) };
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Transport(handler).GetAsync(eTag: null, stop.Token));
    }

    [Fact]
    public async Task The_body_is_read_as_utf8()
    {
        var body = Snapshots.Json().Replace("\"flags\"", "\"note\":\"promoção ✓\",\"flags\"", StringComparison.Ordinal);

        var response = await Transport(new StubHandler(_ => Ok(body, eTag: null))).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal(body, response.Body);
    }

    [Theory]
    [InlineData(0, SnapshotResponseKind.Fetched)]
    [InlineData(1, SnapshotResponseKind.Failed)]
    public async Task A_streamed_body_without_length_stops_at_the_limit(int bytesOverTheLimit, SnapshotResponseKind expected)
    {
        var body = new byte[HttpSnapshotTransport.MaxSnapshotBytes + bytesOverTheLimit];
        Array.Fill(body, (byte)' ');
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnseekableStream(body)) });

        var response = await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Kind);
    }

    [Fact]
    public async Task A_connection_dropped_in_the_middle_of_the_body_is_a_failure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DroppingStream()) });

        var response = await Transport(handler).GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)null), (response.Kind, response.StatusCode));
    }

    [Fact]
    public async Task A_body_that_never_ends_fails_when_the_attempt_times_out()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
        var transport = new HttpSnapshotTransport(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.toggleforge.test/") }, SdkCredential.Parse(Snapshots.Credential), TimeSpan.FromMilliseconds(50));

        var response = await transport.GetAsync(eTag: null, TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotResponseKind.Failed, (int?)null), (response.Kind, response.StatusCode));
    }

    private static HttpSnapshotTransport Transport(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.toggleforge.test/") }, SdkCredential.Parse(Snapshots.Credential));

    private static HttpResponseMessage Ok(string body, string? eTag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (eTag is not null)
        {
            response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(eTag);
        }

        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Sem seek, o <see cref="StreamContent"/> não sabe o tamanho: o corpo chega como numa resposta chunked.</summary>
    private sealed class UnseekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }

    /// <summary>A conexão cai depois dos headers, no meio do corpo.</summary>
    private sealed class DroppingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("The connection was reset.");
    }

    /// <summary>Um servidor que manda os headers e nunca termina o corpo.</summary>
    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    /// <summary>Um corpo sem tamanho conhecido (não navegável) que conta quantos bytes o transporte leu.</summary>
    private sealed class CountingStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
