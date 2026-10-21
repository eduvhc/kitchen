namespace MediatingKit.UnitTests;

[TestClass]
public sealed class PublishAndStreamTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task A_notification_reaches_its_own_handlers_then_those_for_its_interfaces()
    {
        await using var host = TestHost.Create();

        await host.Mediator.Publish(new FileDeleted("f1"), Ct);

        // Exact-type handlers first (ordinal by name), then the interface, then INotification.
        CollectionAssert.AreEqual(
            new[] { "audit f1", "purge f1", "index f1", "count FileDeleted" },
            host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_notification_with_only_interface_handlers_reaches_them()
    {
        await using var host = TestHost.Create();

        await host.Mediator.Publish(new FileRenamed("f2", "g2"), Ct);

        CollectionAssert.AreEqual(new[] { "index f2", "count FileRenamed" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_notification_typed_as_its_interface_is_dispatched_by_its_runtime_type()
    {
        await using var host = TestHost.Create();
        IFileEvent notification = new FileDeleted("f3");

        await host.Mediator.Publish(notification, Ct);

        Assert.HasCount(4, host.Journal.Entries);
    }

    [TestMethod]
    public async Task A_notification_only_the_catch_all_handles_reaches_it()
    {
        await using var host = TestHost.Create();

        await host.Mediator.Publish(new NobodyListens(), Ct);

        CollectionAssert.AreEqual(new[] { "count NobodyListens" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task Sequential_publishing_stops_at_the_first_failure()
    {
        await using var host = TestHost.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Publish(new FileDeleted("fail"), Ct));

        CollectionAssert.AreEqual(new[] { "audit fail" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task Parallel_publishing_runs_every_handler_even_when_one_fails()
    {
        await using var host = TestHost.Create(o => o.NotificationPublishing = NotificationPublishing.Parallel);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Publish(new FileDeleted("fail"), Ct));

        CollectionAssert.AreEquivalent(new[] { "audit fail", "index fail", "count FileDeleted" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task Parallel_publishing_completes_when_every_handler_succeeds()
    {
        await using var host = TestHost.Create(o => o.NotificationPublishing = NotificationPublishing.Parallel);

        await host.Mediator.Publish(new FileDeleted("ok"), Ct);

        Assert.HasCount(4, host.Journal.Entries);
    }

    [TestMethod]
    public async Task A_stream_request_yields_its_items()
    {
        await using var host = TestHost.Create();
        var items = new List<int>();

        await foreach (var item in host.Mediator.CreateStream(new CountTo(3), Ct))
        {
            items.Add(item);
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, items);
    }

    [TestMethod]
    public async Task A_stream_stops_when_cancelled()
    {
        await using var host = TestHost.Create();
        using var cts = new CancellationTokenSource();
        var items = new List<int>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in host.Mediator.CreateStream(new CountTo(100), cts.Token))
            {
                items.Add(item);

                if (item == 2)
                {
                    await cts.CancelAsync();
                }
            }
        });

        CollectionAssert.AreEqual(new[] { 1, 2 }, items);
    }
}
