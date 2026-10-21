using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ResultingKit;

namespace StoringKit.UnitTests;

[TestClass]
public sealed class ObjectStoreJsonTests
{
    private static readonly Order Ada = new("ord-1", "Ada Lovelace", 3, "open");

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task A_document_round_trips_as_web_json_with_its_etag()
    {
        using var store = new FakeObjectStore();

        var put = await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);
        var get = await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct);

        Assert.IsTrue(put.IsSuccess, put.ToString());
        Assert.IsTrue(get.IsSuccess, get.ToString());
        Assert.AreEqual(Ada, get.Value.Value);
        Assert.AreEqual(put.Value.ETag, get.Value.ETag);
        Assert.AreEqual(put.Value.ETag, get.Value.Properties.ETag);
        Assert.AreEqual("application/json", get.Value.Properties.ContentType);
        Assert.AreEqual("""{"id":"ord-1","customer":"Ada Lovelace","quantity":3,"status":"open"}""", await StoredTextAsync(store, "orders/1.json"));
    }

    [TestMethod]
    public async Task A_put_sends_a_seekable_body_so_its_md5_is_computed()
    {
        using var store = new FakeObjectStore();

        var put = await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);

        var write = store.Writes.Single();
        Assert.IsNotNull(write.ContentMd5);
        Assert.AreEqual(put.Value.ContentMd5, write.ContentMd5);
        Assert.AreEqual(put.Value.Size, write.Length);
    }

    [TestMethod]
    public async Task Put_options_pass_through_without_being_changed()
    {
        using var store = new FakeObjectStore();
        var options = new PutObjectOptions { Condition = WriteCondition.IfNotExists };
        options.Metadata["kind"] = "order";

        await store.PutAsJsonAsync("orders/1.json", Ada, options, ct: Ct);

        var write = store.Writes.Single();
        Assert.AreEqual("application/json", write.ContentType);
        Assert.AreSame(WriteCondition.IfNotExists, write.Condition);
        Assert.AreEqual("order", write.Metadata["kind"]);
        Assert.IsNull(options.ContentType);
        Assert.HasCount(1, options.Metadata);
    }

    [TestMethod]
    public async Task A_content_type_the_caller_sets_is_kept()
    {
        using var store = new FakeObjectStore();

        await store.PutAsJsonAsync("orders/1.json", Ada, new PutObjectOptions { ContentType = "application/vnd.acme.order+json" }, ct: Ct);

        Assert.AreEqual("application/vnd.acme.order+json", store.Writes.Single().ContentType);
    }

    [TestMethod]
    public async Task Serializer_options_apply_to_both_directions()
    {
        using var store = new FakeObjectStore();
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        await store.PutAsJsonAsync("orders/1.json", new Shipment("ord-1", "DHL"), jsonOptions: json, ct: Ct);
        var get = await store.GetFromJsonAsync<Shipment>("orders/1.json", json, Ct);

        Assert.AreEqual("""{"order_id":"ord-1","carrier_name":"DHL"}""", await StoredTextAsync(store, "orders/1.json"));
        Assert.AreEqual(new Shipment("ord-1", "DHL"), get.Value.Value);
    }

    [TestMethod]
    public async Task Source_generated_metadata_round_trips_with_its_own_options()
    {
        using var store = new FakeObjectStore();

        var put = await store.PutAsJsonAsync("orders/1.json", Ada, TestJsonContext.Default.Order, ct: Ct);
        var get = await store.GetFromJsonAsync("orders/1.json", TestJsonContext.Default.Order, Ct);

        Assert.IsTrue(put.IsSuccess, put.ToString());
        Assert.AreEqual(Ada, get.Value.Value);
        Assert.AreEqual(put.Value.ETag, get.Value.ETag);
        Assert.AreEqual("""{"ID":"ord-1","CUSTOMER":"Ada Lovelace","QUANTITY":3,"STATUS":"open"}""", await StoredTextAsync(store, "orders/1.json"));
    }

    [TestMethod]
    public async Task A_create_only_put_loses_to_an_existing_document()
    {
        using var store = new FakeObjectStore();
        var create = new PutObjectOptions { Condition = WriteCondition.IfNotExists };

        var first = await store.PutAsJsonAsync("orders/1.json", Ada, create, ct: Ct);
        var second = await store.PutAsJsonAsync("orders/1.json", Ada with { Status = "dup" }, create, ct: Ct);

        Assert.IsTrue(first.IsSuccess, first.ToString());
        ObjectStoreTests.AssertError(second, StoringErrors.AlreadyExists, ErrorKind.Conflict);
        Assert.AreEqual("open", (await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct)).Value.Value!.Status);
    }

    [TestMethod]
    public async Task A_write_back_with_the_read_etag_wins_once_and_then_goes_stale()
    {
        using var store = new FakeObjectStore();
        await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);
        var read = (await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct)).Value;

        var done = await store.PutAsJsonAsync("orders/1.json", read.Value! with { Status = "done" },
            new PutObjectOptions { Condition = WriteCondition.IfMatch(read.ETag) }, ct: Ct);
        var stale = await store.PutAsJsonAsync("orders/1.json", read.Value! with { Status = "cancelled" },
            new PutObjectOptions { Condition = WriteCondition.IfMatch(read.ETag) }, ct: Ct);

        Assert.IsTrue(done.IsSuccess, done.ToString());
        ObjectStoreTests.AssertError(stale, StoringErrors.PreconditionFailed, ErrorKind.Conflict);
        Assert.AreEqual("done", (await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct)).Value.Value!.Status);
    }

    [TestMethod]
    public async Task A_missing_document_is_not_found()
    {
        using var store = new FakeObjectStore();

        var get = await store.GetFromJsonAsync<Order>("orders/missing.json", ct: Ct);

        ObjectStoreTests.AssertError(get, StoringErrors.NotFound, ErrorKind.NotFound);
        Assert.IsEmpty(store.Opened);
    }

    [TestMethod]
    public async Task An_invalid_key_is_a_validation_error_for_both_directions()
    {
        using var store = new FakeObjectStore();

        ObjectStoreTests.AssertError(await store.PutAsJsonAsync("a//b", Ada, ct: Ct), StoringErrors.InvalidKey, ErrorKind.Validation);
        ObjectStoreTests.AssertError(await store.GetFromJsonAsync<Order>("a//b", ct: Ct), StoringErrors.InvalidKey, ErrorKind.Validation);
        Assert.IsEmpty(store.Writes);
    }

    [TestMethod]
    [DataRow("{\"id\":", DisplayName = "truncated")]
    [DataRow("not json", DisplayName = "not json")]
    [DataRow("[1,2,3]", DisplayName = "wrong shape")]
    [DataRow("{\"quantity\":\"many\"}", DisplayName = "wrong property type")]
    public async Task A_malformed_document_is_an_invalid_document_and_releases_the_stream(string stored)
    {
        using var store = new FakeObjectStore();
        await store.PutAsync("orders/1.json", new MemoryStream(Encoding.UTF8.GetBytes(stored)), ct: Ct);

        var get = await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct);

        ObjectStoreTests.AssertError(get, StoringErrors.InvalidDocument, ErrorKind.Validation);
        StringAssert.Contains(get.Error!.Message, "orders/1.json");
        Assert.IsFalse(store.Opened.Single().CanRead, "the response stream was not disposed");
    }

    [TestMethod]
    public async Task A_value_that_cannot_be_serialized_is_an_invalid_document_and_nothing_is_written()
    {
        using var store = new FakeObjectStore();
        var cycle = new Dictionary<string, object>();
        cycle["self"] = cycle;

        var put = await store.PutAsJsonAsync("orders/1.json", cycle, ct: Ct);

        ObjectStoreTests.AssertError(put, StoringErrors.InvalidDocument, ErrorKind.Validation);
        Assert.IsEmpty(store.Writes);
    }

    [TestMethod]
    public async Task A_json_null_reads_as_default()
    {
        using var store = new FakeObjectStore();
        await store.PutAsJsonAsync<Order?>("orders/1.json", null, ct: Ct);

        var get = await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct);

        Assert.IsTrue(get.IsSuccess, get.ToString());
        Assert.IsNull(get.Value.Value);
        Assert.AreEqual("null", await StoredTextAsync(store, "orders/1.json"));
    }

    [TestMethod]
    public async Task A_connection_lost_mid_read_is_transient_and_releases_the_stream()
    {
        using var store = new FakeObjectStore();
        await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);
        store.OpenRead = _ => new FailingStream(new IOException("reset", new SocketException()));

        var get = await store.GetFromJsonAsync<Order>("orders/1.json", ct: Ct);

        ObjectStoreTests.AssertError(get, StoringErrors.Unavailable, ErrorKind.Transient);
        Assert.IsFalse(store.Opened.Single().CanRead, "the response stream was not disposed");
    }

    [TestMethod]
    public async Task Caller_cancellation_during_the_read_propagates_and_releases_the_stream()
    {
        using var store = new FakeObjectStore();
        await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.GetFromJsonAsync<Order>("orders/1.json", ct: cts.Token));

        Assert.IsFalse(store.Opened.Single().CanRead, "the response stream was not disposed");
    }

    [TestMethod]
    public async Task Read_from_json_reads_an_open_object_and_leaves_disposal_to_the_caller()
    {
        using var store = new FakeObjectStore();
        await store.PutAsJsonAsync("orders/1.json", Ada, ct: Ct);

        await using var content = (await store.GetAsync("orders/1.json", Ct)).Value;
        var order = await content.ReadFromJsonAsync<Order>(ct: Ct);

        Assert.IsTrue(order.IsSuccess, order.ToString());
        Assert.AreEqual(Ada, order.Value);
        Assert.IsTrue(content.Content.CanRead);
    }

    [TestMethod]
    public async Task Read_from_json_takes_options_and_source_generated_metadata()
    {
        using var store = new FakeObjectStore();
        await store.PutAsync("orders/1.json", new MemoryStream("""{"order_id":"ord-1","carrier_name":"DHL"}"""u8.ToArray()), ct: Ct);
        await store.PutAsJsonAsync("orders/2.json", Ada, TestJsonContext.Default.Order, ct: Ct);

        await using var shipment = (await store.GetAsync("orders/1.json", Ct)).Value;
        await using var order = (await store.GetAsync("orders/2.json", Ct)).Value;

        Assert.AreEqual(
            new Shipment("ord-1", "DHL"),
            (await shipment.ReadFromJsonAsync<Shipment>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }, Ct)).Value);
        Assert.AreEqual(Ada, (await order.ReadFromJsonAsync(TestJsonContext.Default.Order, Ct)).Value);
    }

    [TestMethod]
    public async Task Read_from_json_returns_errors_for_malformed_content_and_lost_connections()
    {
        using var store = new FakeObjectStore();
        await store.PutAsync("orders/1.json", new MemoryStream("{"u8.ToArray()), ct: Ct);

        await using (var malformed = (await store.GetAsync("orders/1.json", Ct)).Value)
        {
            ObjectStoreTests.AssertError(await malformed.ReadFromJsonAsync<Order>(ct: Ct), StoringErrors.InvalidDocument, ErrorKind.Validation);
        }

        store.OpenRead = _ => new FailingStream(new IOException("reset", new SocketException()));
        await using var dropped = (await store.GetAsync("orders/1.json", Ct)).Value;

        ObjectStoreTests.AssertError(await dropped.ReadFromJsonAsync(TestJsonContext.Default.Order, Ct), StoringErrors.Unavailable, ErrorKind.Transient);
    }

    private async Task<string> StoredTextAsync(FakeObjectStore store, string key)
    {
        await using var content = (await store.GetAsync(key, Ct)).Value;
        using var reader = new StreamReader(content.Content);
        return await reader.ReadToEndAsync(Ct);
    }

    private sealed class FailingStream(Exception failure) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(failure);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException<int>(failure);
    }
}

public sealed record Order(string Id, string Customer, int Quantity, string Status);

public sealed record Shipment(string OrderId, string CarrierName);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseUpper)]
[JsonSerializable(typeof(Order))]
internal sealed partial class TestJsonContext : JsonSerializerContext;
