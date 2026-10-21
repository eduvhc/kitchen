namespace MediatingKit.UnitTests;

[TestClass]
public sealed class SendTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task A_request_reaches_its_handler_and_the_response_type_is_inferred()
    {
        await using var host = TestHost.Create();

        Result result = await host.Mediator.Send(new DeleteFile("abc"), Ct);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(new[] { "delete abc" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_failed_result_comes_back_as_a_value_not_an_exception()
    {
        await using var host = TestHost.Create();

        var result = await host.Mediator.Send(new DeleteFile("missing"), Ct);

        Assert.IsTrue(result.IsFailure);
        Assert.AreEqual("file.not_found", result.Error!.Code);
        Assert.AreEqual(ErrorKind.NotFound, result.Error.Kind);
    }

    [TestMethod]
    public async Task A_typed_result_carries_its_value()
    {
        await using var host = TestHost.Create();

        Result<StoredFile> found = await host.Mediator.Send(new GetFile("a"), Ct);
        var missing = await host.Mediator.Send(new GetFile("missing"), Ct);

        Assert.AreEqual(new StoredFile("a", 42), found.Value);
        Assert.AreEqual(ErrorKind.NotFound, missing.Error!.Kind);
    }

    [TestMethod]
    public async Task Value_type_requests_and_responses_work()
    {
        await using var host = TestHost.Create();

        int sum = await host.Mediator.Send(new Sum(2, 3), Ct);

        Assert.AreEqual(5, sum);
    }

    [TestMethod]
    public async Task A_request_without_a_response_reaches_its_handler()
    {
        await using var host = TestHost.Create();

        await host.Mediator.Send(new Touch("t1"), Ct);

        CollectionAssert.AreEqual(new[] { "touch t1" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_request_without_a_response_can_also_be_sent_for_its_unit()
    {
        await using var host = TestHost.Create();

        var unit = await host.Mediator.Send<Unit>(new Touch("t2"), Ct);

        Assert.AreEqual(Unit.Value, unit);
        CollectionAssert.AreEqual(new[] { "touch t2" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task An_explicitly_implemented_handler_is_reached()
    {
        await using var host = TestHost.Create();

        Assert.AreEqual("hi", await host.Mediator.Send(new Echo("hi"), Ct));
    }

    [TestMethod]
    public async Task A_derived_request_reaches_the_handler_for_its_base_type()
    {
        await using var host = TestHost.Create();

        Assert.AreEqual("shape circle", await host.Mediator.Send(new Circle(2), Ct));
        Assert.AreEqual("shape square", await host.Mediator.Send(new Shape("square"), Ct));
    }

    [TestMethod]
    public async Task The_mediator_resolves_as_ISender_and_IPublisher_too()
    {
        await using var host = TestHost.Create();
        using var scope = host.Root.CreateScope();

        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        Assert.AreSame(mediator, scope.ServiceProvider.GetRequiredService<ISender>());
        Assert.AreSame(mediator, scope.ServiceProvider.GetRequiredService<IPublisher>());
        Assert.AreEqual(8, await scope.ServiceProvider.GetRequiredService<ISender>().Send(new Ping(7), Ct));
    }

    [TestMethod]
    public async Task Cancellation_reaches_the_handler()
    {
        await using var host = TestHost.Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await host.Mediator.Send(new Wait(TimeSpan.FromSeconds(30)), cts.Token));
    }

    [TestMethod]
    public async Task Cancellation_reaches_the_handler_through_behaviors()
    {
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(TraceBehavior<,>)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await host.Mediator.Send(new Wait(TimeSpan.FromSeconds(30)), cts.Token));
    }

    [TestMethod]
    public async Task Handler_exceptions_reach_the_caller_whether_thrown_before_or_after_an_await()
    {
        await using var host = TestHost.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Send(new Explode(false), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Send(new Explode(true), Ct));
    }

    [TestMethod]
    public async Task A_request_nothing_handles_throws_naming_the_type()
    {
        await using var host = TestHost.Create();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => await host.Mediator.Send(new Unhandled<int>(), Ct));

        StringAssert.Contains(error.Message, "Unhandled");
    }

    [TestMethod]
    public async Task A_null_request_throws_ArgumentNullException()
    {
        await using var host = TestHost.Create();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await host.Mediator.Send((DeleteFile)null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await host.Mediator.Send((Touch)null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await host.Mediator.Publish((FileDeleted)null!, Ct));
        Assert.ThrowsExactly<ArgumentNullException>(() => host.Mediator.CreateStream((CountTo)null!, Ct));
    }
}
