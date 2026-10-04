namespace ToggleForge.Sdk.Tests;

public sealed class FileSnapshotStoreTests : IDisposable
{
    private const string OtherCredential = "tfsdk_otherxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

    private static readonly DateTimeOffset _confirmedAt = new(2026, 10, 4, 12, 30, 15, 123, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "toggleforge-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_saved_snapshot_comes_back_as_it_was()
    {
        var store = Store(Snapshots.Credential);

        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(version: 3), "\"v3\"", _confirmedAt), TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal((Snapshots.Json(version: 3), "\"v3\"", _confirmedAt), (loaded.Body, loaded.ETag, loaded.ConfirmedAt));
    }

    [Fact]
    public async Task A_snapshot_without_etag_comes_back_without_one()
    {
        var store = Store(Snapshots.Credential);

        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(), eTag: null, _confirmedAt), TestContext.Current.CancellationToken);

        Assert.Null((await store.LoadAsync(TestContext.Current.CancellationToken))?.ETag);
    }

    [Fact]
    public async Task A_new_save_replaces_the_previous_one_without_leaving_temporary_files()
    {
        var store = Store(Snapshots.Credential);
        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(version: 3), "\"v3\"", _confirmedAt), TestContext.Current.CancellationToken);

        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(version: 4), "\"v4\"", _confirmedAt.AddHours(1)), TestContext.Current.CancellationToken);

        Assert.Equal("\"v4\"", (await store.LoadAsync(TestContext.Current.CancellationToken))?.ETag);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Nothing_saved_yet_loads_as_nothing() =>
        Assert.Null(await Store(Snapshots.Credential).LoadAsync(TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"format":1,"eTag":null,"confirmedAt":"2026""")]
    [InlineData("""{"eTag":null,"confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":"{}"}""")]
    [InlineData("""{"format":"1","eTag":null,"confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":"{}"}""")]
    [InlineData("""{"format":1,"eTag":null,"confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":{}}""")]
    [InlineData("""{"format":1,"eTag":null,"confirmedAt":1791117600,"snapshot":"{}"}""")]
    [InlineData("""{"format":2,"eTag":null,"confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":"{}"}""")]
    [InlineData("""{"format":1,"eTag":null,"confirmedAt":"yesterday","snapshot":"{}"}""")]
    [InlineData("""{"format":1,"eTag":null,"confirmedAt":"2026-10-04T12:00:00.0000000+00:00"}""")]
    [InlineData("""{"format":1,"eTag":null,"snapshot":"{}"}""")]
    [InlineData("""{"format":1,"eTag":"v1","confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":"{}"}""")]
    [InlineData("""{"format":1,"eTag":"\"v 1\"","confirmedAt":"2026-10-04T12:00:00.0000000+00:00","snapshot":"{}"}""")]
    public async Task A_file_this_sdk_did_not_write_loads_as_nothing(string content)
    {
        var store = Store(Snapshots.Credential);
        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(), "\"v1\"", _confirmedAt), TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(Assert.Single(Directory.GetFiles(_directory)), content, TestContext.Current.CancellationToken);

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_etag_that_is_not_text_is_dropped_and_the_snapshot_still_loads()
    {
        var store = Store(Snapshots.Credential);
        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(), "\"v1\"", _confirmedAt), TestContext.Current.CancellationToken);
        var content = """{"format":1,"eTag":7,"confirmedAt":"2026-10-04T12:30:15.1230000+00:00","snapshot":"{}"}""";

        await File.WriteAllTextAsync(Assert.Single(Directory.GetFiles(_directory)), content, TestContext.Current.CancellationToken);
        var loaded = await store.LoadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(("{}", (string?)null, _confirmedAt), (loaded.Body, loaded.ETag, loaded.ConfirmedAt));
    }

    [Fact]
    public async Task A_weak_etag_from_the_api_comes_back_as_it_was()
    {
        var store = Store(Snapshots.Credential);

        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(), "W/\"v1\"", _confirmedAt), TestContext.Current.CancellationToken);

        Assert.Equal("W/\"v1\"", (await store.LoadAsync(TestContext.Current.CancellationToken))?.ETag);
    }

    [Fact]
    public async Task A_file_far_bigger_than_any_snapshot_is_not_read()
    {
        var store = Store(Snapshots.Credential);
        await store.SaveAsync(new StoredSnapshot(Snapshots.Json(), "\"v1\"", _confirmedAt), TestContext.Current.CancellationToken);
        var file = Assert.Single(Directory.GetFiles(_directory));

        await File.AppendAllTextAsync(file, new string(' ', (4 * HttpSnapshotTransport.MaxSnapshotBytes) + 1), TestContext.Current.CancellationToken);

        Assert.Null(await store.LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_clients_of_the_same_credential_saving_at_once_leave_a_readable_file()
    {
        var first = Store(Snapshots.Credential);
        var second = Store(Snapshots.Credential);

        await Task.WhenAll(Enumerable.Range(1, 40).Select(version =>
            Task.Run(() => (version % 2 == 0 ? first : second).SaveAsync(
                new StoredSnapshot(Snapshots.Json(version), $"\"v{version}\"", _confirmedAt), TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.NotNull(await first.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Each_credential_keeps_its_own_snapshot()
    {
        await Store(Snapshots.Credential).SaveAsync(new StoredSnapshot(Snapshots.Json(), "\"dev\"", _confirmedAt), TestContext.Current.CancellationToken);

        Assert.Null(await Store(OtherCredential).LoadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_credential_never_reaches_the_disk()
    {
        await Store(Snapshots.Credential).SaveAsync(new StoredSnapshot(Snapshots.Json(), "\"v1\"", _confirmedAt), TestContext.Current.CancellationToken);

        var file = Assert.Single(Directory.GetFiles(_directory));
        Assert.DoesNotContain("CANARY", file, StringComparison.Ordinal);
        Assert.DoesNotContain("CANARY", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private FileSnapshotStore Store(string credential) => new(_directory, SdkCredential.Parse(credential));
}
