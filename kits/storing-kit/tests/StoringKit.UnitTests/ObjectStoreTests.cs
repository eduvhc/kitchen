using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using ResultingKit;

namespace StoringKit.UnitTests;

[TestClass]
public sealed class ObjectStoreTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("a")]
    [DataRow("photos/2026/ada.jpg")]
    [DataRow("with space/and-dash_and.dot.txt")]
    [DataRow("relatórios/ü—€.txt")]
    [DataRow(".hidden")]
    public async Task Accepts_portable_keys(string key)
    {
        using var store = new FakeObjectStore();

        var result = await store.PutAsync(key, Bytes("x"), ct: TestContext.CancellationToken);

        Assert.IsTrue(result.IsSuccess, result.ToString());
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow("/leading")]
    [DataRow("trailing/")]
    [DataRow("trailing.")]
    [DataRow("a//b")]
    [DataRow("a/./b")]
    [DataRow("a/../b")]
    [DataRow("..")]
    [DataRow("back\\slash")]
    [DataRow("tab\there")]
    [DataRow("new\nline")]
    public async Task Refuses_keys_providers_would_rewrite_or_reject(string key)
    {
        using var store = new FakeObjectStore();

        AssertError(await store.PutAsync(key, Bytes("x"), ct: TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.GetAsync(key, TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.GetPropertiesAsync(key, TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.ExistsAsync(key, TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.DeleteAsync(key, TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.GetReadUrlAsync(key, TimeSpan.FromMinutes(1), TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        Assert.IsEmpty(store.Writes);
    }

    [TestMethod]
    public async Task Refuses_keys_over_1024_utf8_bytes_and_unpaired_surrogates()
    {
        using var store = new FakeObjectStore();

        Assert.IsTrue((await store.PutAsync(new string('a', 1024), Bytes("x"), ct: TestContext.CancellationToken)).IsSuccess);
        AssertError(await store.PutAsync(new string('a', 1025), Bytes("x"), ct: TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.PutAsync(new string('é', 513), Bytes("x"), ct: TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        AssertError(await store.PutAsync("bad\uD800", Bytes("x"), ct: TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
    }

    [TestMethod]
    public async Task A_null_key_or_content_is_a_bug_not_a_result()
    {
        using var store = new FakeObjectStore();

        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.PutAsync(null!, Bytes("x")));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.PutAsync("k", null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => store.GetAsync(null!));
    }

    [TestMethod]
    public async Task Metadata_names_are_lower_cased_and_values_kept()
    {
        using var store = new FakeObjectStore();
        var options = new PutObjectOptions();
        options.Metadata["Tenant_Id"] = "Acme Corp";
        options.Metadata["_x1"] = "1";

        await store.PutAsync("k", Bytes("x"), options, TestContext.CancellationToken);

        var metadata = store.Writes.Single().Metadata;
        Assert.HasCount(2, metadata);
        Assert.AreEqual("Acme Corp", metadata["tenant_id"]);
        Assert.AreEqual("1", metadata["_x1"]);
    }

    [TestMethod]
    [DataRow("1st", "v", DisplayName = "name starts with a digit")]
    [DataRow("has-dash", "v", DisplayName = "name has a hyphen")]
    [DataRow("naïve", "v", DisplayName = "name is not ASCII")]
    [DataRow("", "v", DisplayName = "name is empty")]
    [DataRow("ok", "", DisplayName = "value is empty")]
    [DataRow("ok", " padded", DisplayName = "value has a leading space")]
    [DataRow("ok", "café", DisplayName = "value is not ASCII")]
    [DataRow("ok", "line\nbreak", DisplayName = "value has a control character")]
    public async Task Refuses_metadata_every_provider_cannot_store(string name, string value)
    {
        using var store = new FakeObjectStore();
        var options = new PutObjectOptions();
        options.Metadata[name] = value;

        AssertError(await store.PutAsync("k", Bytes("x"), options, TestContext.CancellationToken), StoringErrors.InvalidMetadata, ErrorKind.Validation);
        Assert.IsEmpty(store.Writes);
    }

    [TestMethod]
    public async Task Refuses_metadata_names_that_differ_only_by_case_and_oversized_metadata()
    {
        using var store = new FakeObjectStore();

        var duplicate = new PutObjectOptions();
        duplicate.Metadata["Name"] = "a";
        duplicate.Metadata["name"] = "b";
        AssertError(await store.PutAsync("k", Bytes("x"), duplicate, TestContext.CancellationToken), StoringErrors.InvalidMetadata, ErrorKind.Validation);

        var large = new PutObjectOptions();
        large.Metadata["big"] = new string('v', ObjectStore.MaxMetadataBytes);
        AssertError(await store.PutAsync("k", Bytes("x"), large, TestContext.CancellationToken), StoringErrors.InvalidMetadata, ErrorKind.Validation);

        var fits = new PutObjectOptions();
        fits.Metadata["big"] = new string('v', ObjectStore.MaxMetadataBytes - 3);
        Assert.IsTrue((await store.PutAsync("k", Bytes("x"), fits, TestContext.CancellationToken)).IsSuccess);
    }

    [TestMethod]
    public async Task Computes_the_md5_of_a_seekable_stream_from_its_position_and_rewinds()
    {
        using var store = new FakeObjectStore();
        var stream = Bytes("skip:keep");
        stream.Position = 5;

        var result = await store.PutAsync("k", stream, ct: TestContext.CancellationToken);

        Assert.AreEqual(Md5("keep"), result.Value.ContentMd5);
        Assert.AreEqual(4, result.Value.Size);
        Assert.AreEqual(4, store.Writes.Single().Length);
    }

    [TestMethod]
    public async Task Leaves_the_md5_of_a_non_seekable_stream_to_the_provider()
    {
        using var store = new FakeObjectStore();

        var result = await store.PutAsync("k", new NonSeekable(Encoding.UTF8.GetBytes("abc")), ct: TestContext.CancellationToken);

        Assert.IsNull(store.Writes.Single().ContentMd5);
        Assert.IsNull(store.Writes.Single().Length);
        Assert.AreEqual(3, result.Value.Size);
    }

    [TestMethod]
    public async Task Passes_a_supplied_md5_through_and_refuses_a_malformed_one()
    {
        using var store = new FakeObjectStore();
        var supplied = Md5("something else");

        await store.PutAsync("k", Bytes("x"), new PutObjectOptions { ContentMd5 = supplied }, TestContext.CancellationToken);
        Assert.AreEqual(supplied, store.Writes.Single().ContentMd5);

        AssertError(
            await store.PutAsync("k", Bytes("x"), new PutObjectOptions { ContentMd5 = "bm90IG1kNQ==" }, TestContext.CancellationToken),
            StoringErrors.InvalidArgument,
            ErrorKind.Validation);
    }

    [TestMethod]
    public async Task Defaults_the_content_type_and_refuses_a_malformed_one()
    {
        using var store = new FakeObjectStore();

        await store.PutAsync("k", Bytes("x"), ct: TestContext.CancellationToken);
        Assert.AreEqual("application/octet-stream", store.Writes.Single().ContentType);

        AssertError(
            await store.PutAsync("k", Bytes("x"), new PutObjectOptions { ContentType = "text/plain\r\nX-Injected: 1" }, TestContext.CancellationToken),
            StoringErrors.InvalidArgument,
            ErrorKind.Validation);
    }

    [TestMethod]
    public async Task Hands_the_condition_to_the_provider()
    {
        using var store = new FakeObjectStore();

        await store.PutAsync("k", Bytes("x"), new PutObjectOptions { Condition = WriteCondition.IfMatch("\"7\"") }, TestContext.CancellationToken);

        var condition = store.Writes.Single().Condition!;
        Assert.IsFalse(condition.IsCreateOnly);
        Assert.AreEqual("\"7\"", condition.IfMatchETag);
        Assert.IsTrue(WriteCondition.IfNotExists.IsCreateOnly);
        Assert.ThrowsExactly<ArgumentException>(() => WriteCondition.IfMatch(" "));
    }

    [TestMethod]
    public async Task Refuses_page_sizes_providers_do_not_serve()
    {
        using var store = new FakeObjectStore();

        AssertError(await store.ListAsync(options: new ListObjectsOptions { PageSize = 0 }, ct: TestContext.CancellationToken), StoringErrors.InvalidArgument, ErrorKind.Validation);
        AssertError(await store.ListAsync(options: new ListObjectsOptions { PageSize = 1001 }, ct: TestContext.CancellationToken), StoringErrors.InvalidArgument, ErrorKind.Validation);
        AssertError(await store.ListAsync("/abs", ct: TestContext.CancellationToken), StoringErrors.InvalidKey, ErrorKind.Validation);
        Assert.IsTrue((await store.ListAsync("folder/", ct: TestContext.CancellationToken)).IsSuccess);
    }

    [TestMethod]
    public async Task Read_urls_expire_against_the_clock_and_cannot_outlive_seven_days()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var store = new FakeObjectStore(timeProvider: clock);

        var url = await store.GetReadUrlAsync("k", TimeSpan.FromHours(1), TestContext.CancellationToken);

        Assert.IsTrue(url.IsSuccess);
        Assert.AreEqual(clock.GetUtcNow().AddHours(1), store.SignedUrls.Single().ExpiresOn);

        Assert.IsTrue((await store.GetReadUrlAsync("k", TimeSpan.FromDays(7), TestContext.CancellationToken)).IsSuccess);
        AssertError(await store.GetReadUrlAsync("k", TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1), TestContext.CancellationToken), StoringErrors.InvalidArgument, ErrorKind.Validation);
        AssertError(await store.GetReadUrlAsync("k", TimeSpan.Zero, TestContext.CancellationToken), StoringErrors.InvalidArgument, ErrorKind.Validation);
    }

    [TestMethod]
    public void Public_urls_escape_each_segment_under_the_base()
    {
        using var store = new FakeObjectStore(new Uri("https://cdn.example.com/assets"));

        var url = store.GetPublicUrl("avatars/ada lovelace#1?.png");

        Assert.AreEqual("https://cdn.example.com/assets/avatars/ada%20lovelace%231%3F.png", url.Value.AbsoluteUri);
    }

    [TestMethod]
    public void A_store_without_a_public_base_url_refuses_to_make_one_up()
    {
        using var store = new FakeObjectStore();

        Assert.ThrowsExactly<InvalidOperationException>(() => store.GetPublicUrl("k"));
        AssertError(store.GetPublicUrl("/bad"), StoringErrors.InvalidKey, ErrorKind.Validation);
    }

    [TestMethod]
    public async Task Exceptions_the_provider_classifies_become_results()
    {
        using var store = new FakeObjectStore();

        AssertError(await store.GetAsync("missing", TestContext.CancellationToken), StoringErrors.NotFound, ErrorKind.NotFound);
        AssertError(await store.GetPropertiesAsync("missing", TestContext.CancellationToken), StoringErrors.NotFound, ErrorKind.NotFound);

        store.Throw = new InvalidDataException("provider-specific");
        store.Translator = _ => StoringErrors.Throttling("slow down");
        AssertError(await store.DeleteAsync("k", TestContext.CancellationToken), StoringErrors.Throttled, ErrorKind.Transient);
    }

    [TestMethod]
    public async Task Transport_failures_are_transient()
    {
        using var store = new FakeObjectStore();

        store.Throw = new HttpRequestException("connection refused");
        AssertError(await store.ExistsAsync("k", TestContext.CancellationToken), StoringErrors.Unavailable, ErrorKind.Transient);

        store.Throw = new TimeoutException();
        AssertError(await store.ExistsAsync("k", TestContext.CancellationToken), StoringErrors.Timeout, ErrorKind.Transient);

        store.Throw = new TaskCanceledException("HttpClient.Timeout elapsed");
        AssertError(await store.ExistsAsync("k", TestContext.CancellationToken), StoringErrors.Timeout, ErrorKind.Transient);

        store.Throw = new AggregateException(new InvalidOperationException("first try"), new HttpRequestException("last try"));
        AssertError(await store.ExistsAsync("k", TestContext.CancellationToken), StoringErrors.Unavailable, ErrorKind.Transient);
    }

    [TestMethod]
    public async Task Unclassified_exceptions_propagate()
    {
        using var store = new FakeObjectStore { Throw = new UnauthorizedAccessException("bad credentials") };

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => store.ListAsync(ct: TestContext.CancellationToken));
    }

    [TestMethod]
    public async Task Caller_cancellation_propagates_instead_of_reading_as_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var store = new FakeObjectStore { Throw = new OperationCanceledException(cts.Token) };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => store.ExistsAsync("k", cts.Token));
    }

    internal static void AssertError(Result result, string code, ErrorKind kind)
    {
        Assert.IsTrue(result.IsFailure, $"Expected {code}, got {result}");
        Assert.AreEqual(code, result.Error.Code);
        Assert.AreEqual(kind, result.Error.Kind);
    }

    private static MemoryStream Bytes(string value) => new(Encoding.UTF8.GetBytes(value));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "The checksum the stores verify.")]
    private static string Md5(string value) => Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
