namespace MediatingKit.UnitTests;

[TestClass]
public sealed class BehaviorTests
{
    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Ct => TestContext.CancellationToken;

    [TestMethod]
    public async Task Behaviors_run_in_registration_order_around_the_handler()
    {
        await using var host = TestHost.Create(o => o
            .AddBehavior(typeof(TraceBehavior<,>))
            .AddBehavior<DeleteFileValidation>());

        await host.Mediator.Send(new DeleteFile("abc"), Ct);

        CollectionAssert.AreEqual(
            new[] { "trace> DeleteFile", "validate", "delete abc", "trace< DeleteFile" },
            host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_closed_behavior_can_short_circuit_with_a_failed_result()
    {
        await using var host = TestHost.Create(o => o.AddBehavior<DeleteFileValidation>());

        var result = await host.Mediator.Send(new DeleteFile(" "), Ct);

        Assert.AreEqual("file.id_required", result.Error!.Code);
        CollectionAssert.AreEqual(new[] { "validate" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_closed_behavior_only_runs_for_its_request()
    {
        await using var host = TestHost.Create(o => o.AddBehavior<DeleteFileValidation>());

        await host.Mediator.Send(new Touch("t"), Ct);

        CollectionAssert.AreEqual(new[] { "touch t" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task A_constrained_open_behavior_runs_only_where_its_constraints_hold()
    {
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(FailureLogBehavior<,>)));

        await host.Mediator.Send(new DeleteFile("missing"), Ct);
        var failed = await host.Mediator.Send(new GetFile("missing"), Ct);
        Assert.AreEqual(4, await host.Mediator.Send(new Sum(2, 2), Ct));

        Assert.IsTrue(failed.IsFailure);
        CollectionAssert.AreEqual(
            new[] { "delete missing", "failed file.not_found", "failed file.not_found" },
            host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task Open_behaviors_wrap_requests_without_a_response_as_unit()
    {
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(TraceBehavior<,>)));

        await host.Mediator.Send(new Touch("t"), Ct);

        CollectionAssert.AreEqual(new[] { "trace> Touch", "touch t", "trace< Touch" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task Behaviors_registered_directly_in_the_container_run_too()
    {
        await using var host = TestHost.Create(
            services: s => s.AddScoped(typeof(IPipelineBehavior<,>), typeof(TraceBehavior<,>)));

        await host.Mediator.Send(new Ping(1), Ct);

        CollectionAssert.AreEqual(new[] { "trace> Ping", "trace< Ping" }, host.Journal.Entries.ToArray());
    }

    [TestMethod]
    public async Task The_pipeline_is_built_once_and_reused()
    {
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(TraceBehavior<,>)));
        var mediator = host.Mediator;

        for (var i = 0; i < 3; i++)
        {
            await mediator.Send(new Ping(i), Ct);
        }

        Assert.HasCount(6, host.Journal.Entries);
    }

    [TestMethod]
    public void Adding_something_that_is_not_a_behavior_fails_fast()
    {
        var options = new MediatorOptions();

#pragma warning disable CA2263 // The Type overload is what is under test.
        Assert.ThrowsExactly<ArgumentException>(() => options.AddBehavior(typeof(string)));
#pragma warning restore CA2263
        Assert.ThrowsExactly<ArgumentException>(() => options.AddBehavior(typeof(IPipelineBehavior<,>)));
        Assert.ThrowsExactly<ArgumentException>(() => options.AddBehavior(typeof(OneParameterBehavior<>)));
        Assert.IsEmpty(options.Behaviors);
    }

    private sealed class OneParameterBehavior<TRequest> : IPipelineBehavior<TRequest, Result>
        where TRequest : notnull
    {
        public ValueTask<Result> Handle(TRequest request, PipelineNext<TRequest, Result> next, CancellationToken ct) => next(request, ct);
    }
}
