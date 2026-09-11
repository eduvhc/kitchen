using TestingKit.MSTest;
using TestingKit.RabbitMq;

namespace TestingKit.IntegrationTests;

[TestClass]
public class RabbitMqFixtureTests : IntegrationTest
{
    private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    protected override TestEnvironment Environment => TestHost.Environment;

    [TestMethod]
    public async Task Round_trips_a_published_message()
    {
        await PublishAsync(new Order("A-1"));

        var order = await TestHost.RabbitMq.ConsumeAsync<Order>(TestHost.Queue, ct: CancellationToken);

        Assert.IsNotNull(order);
        Assert.AreEqual("A-1", order.Reference);
    }

    [TestMethod]
    public async Task Returns_null_when_nothing_is_queued()
    {
        var body = await TestHost.RabbitMq.ConsumeAsync(
            TestHost.Queue,
            TimeSpan.FromMilliseconds(300),
            CancellationToken);

        Assert.IsNull(body);
    }

    [TestMethod]
    public async Task Reset_purges_the_configured_queues()
    {
        await PublishAsync(new Order("to-be-purged"));

        await TestHost.RabbitMq.ResetAsync(CancellationToken);

        var body = await TestHost.RabbitMq.ConsumeAsync(
            TestHost.Queue,
            TimeSpan.FromMilliseconds(300),
            CancellationToken);

        Assert.IsNull(body);
    }

    [TestMethod]
    public async Task Exposes_headers_a_body_only_read_would_drop()
    {
        await PublishAsync(new Order("A-2"), new Dictionary<string, object?> { ["traceparent"] = TraceParent });

        var message = await TestHost.RabbitMq.ConsumeMessageAsync(TestHost.Queue, ct: CancellationToken);

        Assert.IsNotNull(message);
        Assert.Contains("A-2", message.Body);
        Assert.AreEqual(TraceParent, message.GetHeaderString("traceparent"));
        Assert.AreEqual(TestHost.Exchange, message.GetHeaderString("exchange"));
    }

    /// <summary>
    /// An AMQP field table carries strings as <c>byte[]</c>, so a caster gets a surprise and a
    /// consumer has to decode. The raw value stays reachable for anyone who needs the bytes.
    /// </summary>
    [TestMethod]
    public async Task Keeps_header_values_as_the_wire_carries_them()
    {
        await PublishAsync(new Order("A-3"), new Dictionary<string, object?> { ["traceparent"] = TraceParent });

        var message = await TestHost.RabbitMq.ConsumeMessageAsync(TestHost.Queue, ct: CancellationToken);

        Assert.IsNotNull(message);
        Assert.IsInstanceOfType<byte[]>(message.Headers["traceparent"]);
        Assert.AreEqual(TraceParent, message.GetHeaderString("traceparent"));
    }

    [TestMethod]
    public async Task Reading_an_absent_header_is_null_not_a_throw()
    {
        await PublishAsync(new Order("A-4"));

        var message = await TestHost.RabbitMq.ConsumeMessageAsync(TestHost.Queue, ct: CancellationToken);

        Assert.IsNotNull(message);
        Assert.IsNull(message.GetHeaderString("traceparent"));
    }

    private static Task PublishAsync(Order order, IReadOnlyDictionary<string, object?>? headers = null) =>
        TestHost.RabbitMq.PublishAsync(TestHost.Exchange, TestHost.Queue, order, headers, ct: CancellationToken.None);

    private sealed record Order(string Reference);
}
