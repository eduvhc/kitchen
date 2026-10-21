using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;

namespace StoringKit.IntegrationTests;

/// <summary>
/// The behaviour every provider owes <see cref="IObjectStore"/> callers, run against a real emulator per provider.
/// A test that passes for one provider and fails for another is a contract bug, not a provider quirk.
/// </summary>
public abstract class ObjectStoreContract
{
    private static readonly HttpClient Http = new();

    private string _prefix = null!;

    public TestContext TestContext { get; set; } = null!;

    protected abstract IServiceProvider Services { get; }

    protected IObjectStore Store => Services.GetRequiredService<IObjectStore>();

    protected IObjectStore PublicStore => Services.GetRequiredKeyedService<IObjectStore>(TestHost.PublicStore);

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestInitialize]
    public void UseFreshPrefix() => _prefix = $"contract/{Guid.NewGuid():N}/";

    [TestMethod]
    public async Task Put_then_get_round_trips_content_type_metadata_and_checksum()
    {
        var key = Key("docs/report.pdf");
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 hello");
        var options = new PutObjectOptions { ContentType = "application/pdf" };
        options.Metadata["Tenant_Id"] = "acme";
        options.Metadata["source"] = "upload form";

        var put = await Store.PutAsync(key, new MemoryStream(bytes), options, Ct);

        Assert.IsTrue(put.IsSuccess, put.ToString());
        Assert.AreEqual(key, put.Value.Key);
        Assert.AreEqual(bytes.Length, put.Value.Size);
        Assert.AreEqual(Md5(bytes), put.Value.ContentMd5);
        Assert.IsFalse(string.IsNullOrEmpty(put.Value.ETag));

        var get = await Store.GetAsync(key, Ct);
        Assert.IsTrue(get.IsSuccess, get.ToString());

        await using var content = get.Value;
        CollectionAssert.AreEqual(bytes, await ReadAllAsync(content.Content));
        Assert.AreEqual(bytes.Length, content.Properties.Size);
        Assert.AreEqual("application/pdf", content.Properties.ContentType);
        Assert.AreEqual(put.Value.ETag, content.Properties.ETag);
        Assert.AreEqual(Md5(bytes), content.Properties.ContentMd5);
        Assert.HasCount(2, content.Properties.Metadata);
        Assert.AreEqual("acme", content.Properties.Metadata["tenant_id"]);
        Assert.AreEqual("upload form", content.Properties.Metadata["source"]);
        Assert.IsLessThan(TimeSpan.FromMinutes(5), (DateTimeOffset.UtcNow - content.Properties.LastModified).Duration());
    }

    [TestMethod]
    public async Task Properties_match_what_get_returns()
    {
        var key = Key("a.txt");
        var put = await Store.PutAsync(key, Text("hello"), new PutObjectOptions { ContentType = "text/plain" }, Ct);

        var properties = await Store.GetPropertiesAsync(key, Ct);

        Assert.IsTrue(properties.IsSuccess, properties.ToString());
        Assert.AreEqual(5, properties.Value.Size);
        Assert.AreEqual("text/plain", properties.Value.ContentType);
        Assert.AreEqual(put.Value.ETag, properties.Value.ETag);
        Assert.AreEqual(put.Value.ContentMd5, properties.Value.ContentMd5);
        Assert.IsEmpty(properties.Value.Metadata);
    }

    [TestMethod]
    public async Task Content_type_defaults_to_octet_stream()
    {
        var key = Key("blob");
        await Store.PutAsync(key, Text("x"), ct: Ct);

        var properties = await Store.GetPropertiesAsync(key, Ct);

        Assert.AreEqual(ObjectStore.DefaultContentType, properties.Value.ContentType);
    }

    [TestMethod]
    public async Task A_non_seekable_stream_is_stored_whole()
    {
        var key = Key("streamed.bin");
        var bytes = RandomNumberGenerator.GetBytes(300_000);

        var put = await Store.PutAsync(key, new ForwardOnlyStream(bytes), ct: Ct);

        Assert.IsTrue(put.IsSuccess, put.ToString());
        Assert.AreEqual(bytes.Length, put.Value.Size);

        var get = await Store.GetAsync(key, Ct);
        await using var content = get.Value;
        CollectionAssert.AreEqual(bytes, await ReadAllAsync(content.Content));
    }

    [TestMethod]
    public async Task A_stream_is_read_from_its_current_position()
    {
        var key = Key("tail.txt");
        var stream = Text("skip:keep");
        stream.Position = 5;

        var put = await Store.PutAsync(key, stream, ct: Ct);

        Assert.AreEqual(4, put.Value.Size);
        Assert.AreEqual("keep", await ReadTextAsync(key));
    }

    [TestMethod]
    public async Task An_unconditional_put_replaces_the_object()
    {
        var key = Key("replace.txt");
        var first = await Store.PutAsync(key, Text("one"), ct: Ct);
        var second = await Store.PutAsync(key, Text("two"), ct: Ct);

        Assert.IsTrue(second.IsSuccess, second.ToString());
        Assert.AreNotEqual(first.Value.ETag, second.Value.ETag);
        Assert.AreEqual("two", await ReadTextAsync(key));
    }

    [TestMethod]
    public async Task Missing_objects_are_not_found_everywhere_but_exists_and_delete()
    {
        var key = Key("missing");

        var get = await Store.GetAsync(key, Ct);
        var properties = await Store.GetPropertiesAsync(key, Ct);
        var exists = await Store.ExistsAsync(key, Ct);
        var delete = await Store.DeleteAsync(key, Ct);

        AssertError(get, ErrorKind.NotFound, StoringErrors.NotFound);
        AssertError(properties, ErrorKind.NotFound, StoringErrors.NotFound);
        Assert.IsTrue(exists.IsSuccess, exists.ToString());
        Assert.IsFalse(exists.Value);
        Assert.IsTrue(delete.IsSuccess, delete.ToString());
    }

    [TestMethod]
    public async Task Delete_removes_the_object()
    {
        var key = Key("doomed.txt");
        await Store.PutAsync(key, Text("bye"), ct: Ct);
        Assert.IsTrue((await Store.ExistsAsync(key, Ct)).Value);

        var delete = await Store.DeleteAsync(key, Ct);

        Assert.IsTrue(delete.IsSuccess, delete.ToString());
        Assert.IsFalse((await Store.ExistsAsync(key, Ct)).Value);
    }

    [TestMethod]
    public async Task A_create_only_put_loses_to_an_existing_object_and_leaves_it_untouched()
    {
        var key = Key("once.txt");
        var create = new PutObjectOptions { Condition = WriteCondition.IfNotExists };

        var first = await Store.PutAsync(key, Text("first"), create, Ct);
        var second = await Store.PutAsync(key, Text("second"), new PutObjectOptions { Condition = WriteCondition.IfNotExists }, Ct);

        Assert.IsTrue(first.IsSuccess, first.ToString());
        AssertError(second, ErrorKind.Conflict, StoringErrors.AlreadyExists);
        Assert.AreEqual("first", await ReadTextAsync(key));
    }

    [TestMethod]
    public async Task Exactly_one_of_many_concurrent_create_only_puts_wins()
    {
        var key = Key("race.txt");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Store.PutAsync(key, Text($"writer {i}"), new PutObjectOptions { Condition = WriteCondition.IfNotExists }, Ct)));

        Assert.AreEqual(1, results.Count(r => r.IsSuccess), string.Join(", ", results.Select(r => r.ToString())));
        foreach (var loser in results.Where(r => r.IsFailure))
        {
            AssertError(loser, ErrorKind.Conflict, StoringErrors.AlreadyExists);
        }

        var winner = results.Single(r => r.IsSuccess).Value;
        Assert.AreEqual(winner.ETag, (await Store.GetPropertiesAsync(key, Ct)).Value.ETag);
    }

    [TestMethod]
    public async Task An_if_match_put_succeeds_on_the_current_etag_and_loses_on_a_stale_one()
    {
        var key = Key("cas.txt");
        var v1 = await Store.PutAsync(key, Text("v1"), ct: Ct);

        var v2 = await Store.PutAsync(key, Text("v2"), new PutObjectOptions { Condition = WriteCondition.IfMatch(v1.Value.ETag) }, Ct);
        var stale = await Store.PutAsync(key, Text("v3"), new PutObjectOptions { Condition = WriteCondition.IfMatch(v1.Value.ETag) }, Ct);

        Assert.IsTrue(v2.IsSuccess, v2.ToString());
        Assert.AreNotEqual(v1.Value.ETag, v2.Value.ETag);
        AssertError(stale, ErrorKind.Conflict, StoringErrors.PreconditionFailed);
        Assert.AreEqual("v2", await ReadTextAsync(key));
    }

    [TestMethod]
    public async Task An_if_match_put_on_a_missing_object_is_a_conflict()
    {
        var key = Key("gone.txt");
        var put = await Store.PutAsync(key, Text("v1"), ct: Ct);
        await Store.DeleteAsync(key, Ct);

        var result = await Store.PutAsync(key, Text("v2"), new PutObjectOptions { Condition = WriteCondition.IfMatch(put.Value.ETag) }, Ct);

        AssertError(result, ErrorKind.Conflict, StoringErrors.PreconditionFailed);
        Assert.IsFalse((await Store.ExistsAsync(key, Ct)).Value);
    }

    [TestMethod]
    public async Task A_wrong_md5_is_rejected_and_nothing_is_stored()
    {
        var key = Key("corrupt.txt");
        var options = new PutObjectOptions { ContentMd5 = Md5(Encoding.UTF8.GetBytes("something else")) };

        var result = await Store.PutAsync(key, Text("actual"), options, Ct);

        AssertError(result, ErrorKind.Validation, StoringErrors.ChecksumMismatch);
        Assert.IsFalse((await Store.ExistsAsync(key, Ct)).Value);
    }

    [TestMethod]
    public async Task Listing_pages_through_a_prefix_in_key_order()
    {
        string[] names = ["e", "a", "c/2", "c/1", "b"];
        foreach (var name in names)
        {
            await Store.PutAsync(Key($"list/{name}"), Text("x"), ct: Ct);
        }

        await Store.PutAsync(Key("listing-is-not-a-prefix-match"), Text("x"), ct: Ct);

        var seen = new List<ObjectSummary>();
        var options = new ListObjectsOptions { PageSize = 2 };
        var pages = 0;

        do
        {
            var page = await Store.ListAsync(Key("list/"), options, Ct);
            Assert.IsTrue(page.IsSuccess, page.ToString());
            Assert.IsLessThanOrEqualTo(2, page.Value.Objects.Count);

            seen.AddRange(page.Value.Objects);
            options.ContinuationToken = page.Value.ContinuationToken;
            pages++;
        }
        while (options.ContinuationToken is not null);

        Assert.IsGreaterThanOrEqualTo(3, pages);
        CollectionAssert.AreEqual(
            new[] { "a", "b", "c/1", "c/2", "e" }.Select(n => Key($"list/{n}")).ToList(),
            seen.Select(o => o.Key).ToList());
        Assert.IsTrue(seen.All(o => o.Size == 1 && o.ETag.Length > 0));
    }

    [TestMethod]
    public async Task Listing_an_empty_prefix_returns_an_empty_last_page()
    {
        var page = await Store.ListAsync(Key("nothing-here/"), ct: Ct);

        Assert.IsTrue(page.IsSuccess, page.ToString());
        Assert.IsEmpty(page.Value.Objects);
        Assert.IsFalse(page.Value.HasMore);
    }

    [TestMethod]
    public async Task A_read_url_serves_the_object_without_credentials()
    {
        var key = Key("shared/photo 1.jpg");
        await Store.PutAsync(key, Text("jpeg bytes"), new PutObjectOptions { ContentType = "image/jpeg" }, Ct);

        var url = await Store.GetReadUrlAsync(key, TimeSpan.FromMinutes(5), Ct);

        Assert.IsTrue(url.IsSuccess, url.ToString());
        using var response = await Http.GetAsync(url.Value, Ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("jpeg bytes", await response.Content.ReadAsStringAsync(Ct));
        Assert.AreEqual("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task A_read_url_cannot_outlive_seven_days()
    {
        var result = await Store.GetReadUrlAsync(Key("x"), TimeSpan.FromDays(8), Ct);

        AssertError(result, ErrorKind.Validation, StoringErrors.InvalidArgument);
    }

    [TestMethod]
    public async Task A_public_store_serves_objects_at_their_public_url()
    {
        var key = Key("avatars/ada lovelace.png");
        await PublicStore.PutAsync(key, Text("png"), new PutObjectOptions { ContentType = "image/png" }, Ct);

        var url = PublicStore.GetPublicUrl(key);

        Assert.IsTrue(url.IsSuccess, url.ToString());
        using var response = await Http.GetAsync(url.Value, Ct);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("png", await response.Content.ReadAsStringAsync(Ct));
    }

    [TestMethod]
    public async Task A_private_store_has_no_public_url_and_its_objects_need_signing()
    {
        var key = Key("private.txt");
        await Store.PutAsync(key, Text("secret"), ct: Ct);

        Assert.ThrowsExactly<InvalidOperationException>(() => Store.GetPublicUrl(key));

        var signed = await Store.GetReadUrlAsync(key, TimeSpan.FromMinutes(1), Ct);
        var unsigned = new UriBuilder(signed.Value) { Query = string.Empty }.Uri;
        using var response = await Http.GetAsync(unsigned, Ct);
        Assert.IsFalse(response.IsSuccessStatusCode, $"{unsigned} answered {response.StatusCode}");
    }

    [TestMethod]
    public async Task Invalid_keys_are_validation_errors_and_never_reach_the_provider()
    {
        foreach (var key in new[] { "", "/leading", "a//b", "a/../b", "back\\slash", "trailing/" })
        {
            AssertError(await Store.PutAsync(key, Text("x"), ct: Ct), ErrorKind.Validation, StoringErrors.InvalidKey);
            AssertError(await Store.GetAsync(key, Ct), ErrorKind.Validation, StoringErrors.InvalidKey);
        }
    }

    [TestMethod]
    public async Task Keys_with_unicode_and_spaces_round_trip()
    {
        var key = Key("relatórios/2026 Q3 — final.txt");
        await Store.PutAsync(key, Text("ok"), ct: Ct);

        Assert.AreEqual("ok", await ReadTextAsync(key));

        var page = await Store.ListAsync(Key("relatórios/"), ct: Ct);
        Assert.AreEqual(key, page.Value.Objects.Single().Key);
    }

    [TestMethod]
    public async Task A_json_document_round_trips_with_the_etag_of_its_put()
    {
        var key = Key("docs/order.json");
        var options = new PutObjectOptions();
        options.Metadata["kind"] = "order";

        var put = await Store.PutAsJsonAsync(key, new Counter("orders", 41), options, ct: Ct);
        var get = await Store.GetFromJsonAsync<Counter>(key, ct: Ct);

        Assert.IsTrue(put.IsSuccess, put.ToString());
        Assert.IsTrue(get.IsSuccess, get.ToString());
        Assert.AreEqual(new Counter("orders", 41), get.Value.Value);
        Assert.AreEqual(put.Value.ETag, get.Value.ETag);
        Assert.AreEqual("application/json", get.Value.Properties.ContentType);
        Assert.AreEqual(put.Value.ContentMd5, get.Value.Properties.ContentMd5);
        Assert.AreEqual("order", get.Value.Properties.Metadata["kind"]);
        Assert.AreEqual("""{"name":"orders","value":41}""", await ReadTextAsync(key));
    }

    [TestMethod]
    public async Task A_missing_json_document_is_not_found()
    {
        var get = await Store.GetFromJsonAsync<Counter>(Key("docs/missing.json"), ct: Ct);

        AssertError(get, ErrorKind.NotFound, StoringErrors.NotFound);
    }

    [TestMethod]
    public async Task Exactly_one_of_many_concurrent_create_only_json_puts_wins()
    {
        var key = Key("docs/race.json");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Store.PutAsJsonAsync(key, new Counter($"writer {i}", i), new PutObjectOptions { Condition = WriteCondition.IfNotExists }, ct: Ct)));

        Assert.AreEqual(1, results.Count(r => r.IsSuccess), string.Join(", ", results.Select(r => r.ToString())));
        foreach (var loser in results.Where(r => r.IsFailure))
        {
            AssertError(loser, ErrorKind.Conflict, StoringErrors.AlreadyExists);
        }

        var winner = results.Single(r => r.IsSuccess).Value;
        var stored = (await Store.GetFromJsonAsync<Counter>(key, ct: Ct)).Value;
        Assert.AreEqual(winner.ETag, stored.ETag);
        Assert.AreEqual($"writer {stored.Value!.Value}", stored.Value.Name);
    }

    [TestMethod]
    public async Task Of_two_writers_holding_the_same_etag_only_the_first_wins()
    {
        var key = Key("docs/cas.json");
        await Store.PutAsJsonAsync(key, new Counter("hits", 0), ct: Ct);
        var alice = (await Store.GetFromJsonAsync<Counter>(key, ct: Ct)).Value;
        var bob = (await Store.GetFromJsonAsync<Counter>(key, ct: Ct)).Value;

        var first = await Store.PutAsJsonAsync(key, alice.Value! with { Value = 1 }, new PutObjectOptions { Condition = WriteCondition.IfMatch(alice.ETag) }, ct: Ct);
        var second = await Store.PutAsJsonAsync(key, bob.Value! with { Value = 100 }, new PutObjectOptions { Condition = WriteCondition.IfMatch(bob.ETag) }, ct: Ct);

        Assert.IsTrue(first.IsSuccess, first.ToString());
        AssertError(second, ErrorKind.Conflict, StoringErrors.PreconditionFailed);
        var stored = (await Store.GetFromJsonAsync<Counter>(key, ct: Ct)).Value;
        Assert.AreEqual(1, stored.Value!.Value);
        Assert.AreEqual(first.Value.ETag, stored.ETag);
    }

    [TestMethod]
    public async Task Concurrent_read_modify_write_loops_lose_no_update()
    {
        var key = Key("docs/counter.json");
        await Store.PutAsJsonAsync(key, new Counter("hits", 0), ct: Ct);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => IncrementAsync(key)));

        Assert.AreEqual(6, (await Store.GetFromJsonAsync<Counter>(key, ct: Ct)).Value.Value!.Value);
    }

    [TestMethod]
    public async Task A_malformed_json_document_is_an_invalid_document_and_releases_the_connection()
    {
        var key = Key("docs/broken.json");
        await Store.PutAsync(key, Text("""{"name":"orders","value":"""), new PutObjectOptions { ContentType = "application/json" }, Ct);

        for (var i = 0; i < 3; i++)
        {
            AssertError(await Store.GetFromJsonAsync<Counter>(key, ct: Ct), ErrorKind.Validation, StoringErrors.InvalidDocument);
        }

        Assert.IsTrue((await Store.ExistsAsync(key, Ct)).Value);
    }

    private static void AssertError(Result result, ErrorKind kind, string code)
    {
        Assert.IsTrue(result.IsFailure, $"Expected {code}, got {result}");
        Assert.AreEqual(code, result.Error.Code, result.ToString());
        Assert.AreEqual(kind, result.Error.Kind, result.ToString());
    }

    private static MemoryStream Text(string value) => new(Encoding.UTF8.GetBytes(value));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "The checksum the stores verify.")]
    private static string Md5(byte[] bytes) => Convert.ToBase64String(MD5.HashData(bytes));

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private string Key(string name) => _prefix + name;

    // The read-modify-write loop the README documents.
    private async Task IncrementAsync(string key)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var read = await Store.GetFromJsonAsync<Counter>(key, ct: Ct);
            Assert.IsTrue(read.IsSuccess, read.ToString());

            var write = await Store.PutAsJsonAsync(key, read.Value.Value! with { Value = read.Value.Value.Value + 1 },
                new PutObjectOptions { Condition = WriteCondition.IfMatch(read.Value.ETag) }, ct: Ct);

            if (write.Error is not { Code: StoringErrors.PreconditionFailed })
            {
                Assert.IsTrue(write.IsSuccess, write.ToString());
                return;
            }
        }

        Assert.Fail("The increment kept losing the race.");
    }

    private async Task<string> ReadTextAsync(string key)
    {
        var get = await Store.GetAsync(key, Ct);
        Assert.IsTrue(get.IsSuccess, get.ToString());
        await using var content = get.Value;
        return Encoding.UTF8.GetString(await ReadAllAsync(content.Content));
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private int _position;

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
            // Short reads on purpose, like a network stream.
            var n = Math.Min(Math.Min(count, 7_919), bytes.Length - _position);
            Array.Copy(bytes, _position, buffer, offset, n);
            _position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

[TestClass]
public sealed class AzureBlobStoreContractTests : ObjectStoreContract
{
    protected override IServiceProvider Services => TestHost.AzureServices;
}

[TestClass]
public sealed class S3StoreContractTests : ObjectStoreContract
{
    protected override IServiceProvider Services => TestHost.S3Services;
}

public sealed record Counter(string Name, int Value);
