using System.Diagnostics;
using System.Reflection;

namespace MediatingKit.UnitTests;

/// <summary>
/// Once a mediator has resolved a handler and built its pipeline, a send that completes synchronously allocates
/// nothing: no boxing, no closure, no delegate, no task.
/// </summary>
[TestClass]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "Every call here completes synchronously; awaiting would add the allocations being measured.")]
public sealed class AllocationTests
{
    [TestMethod]
    [DoNotParallelize]
    public async Task Send_on_the_hot_path_allocates_nothing()
    {
        SkipUnlessOptimised();
        await using var host = TestHost.Create();
        var mediator = host.Mediator;
        var request = new Ping(1);

        Assert.AreEqual(2, await mediator.Send(request, CancellationToken.None));

        Assert.AreEqual(0L, BytesPerCall(() => mediator.Send(request, CancellationToken.None).GetAwaiter().GetResult()));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Send_through_a_behavior_allocates_nothing()
    {
        SkipUnlessOptimised();
        await using var host = TestHost.Create(o => o.AddBehavior(typeof(PassThroughBehavior<,>)));
        var mediator = host.Mediator;
        var request = new Ping(1);

        Assert.AreEqual(2, await mediator.Send(request, CancellationToken.None));

        Assert.AreEqual(0L, BytesPerCall(() => mediator.Send(request, CancellationToken.None).GetAwaiter().GetResult()));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Send_without_a_response_allocates_nothing()
    {
        SkipUnlessOptimised();
        await using var host = TestHost.Create(o => o.Lifetime = ServiceLifetime.Singleton);
        var mediator = host.Root.GetRequiredService<IMediator>();
        var request = new Nudge();

        await mediator.Send(request, CancellationToken.None);

        Assert.AreEqual(0L, BytesPerCall(() => mediator.Send(request, CancellationToken.None).GetAwaiter().GetResult()));
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task Publish_to_synchronous_handlers_allocates_nothing()
    {
        SkipUnlessOptimised();
        await using var host = TestHost.Create();
        var mediator = host.Mediator;
        var notification = new FileRenamed("a", "b");

        await mediator.Publish(notification, CancellationToken.None);

        var bytes = BytesPerCall(() => mediator.Publish(notification, CancellationToken.None).GetAwaiter().GetResult());

        // Both handlers format a journal entry; allow for those and nothing else.
        Assert.IsLessThanOrEqualTo(160L, bytes, $"{bytes} bytes per publish.");
    }

    private static long BytesPerCall(Action call)
    {
        const int Calls = 1_000;

        call();

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < Calls; i++)
        {
            call();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / Calls;
    }

    private static void SkipUnlessOptimised()
    {
        if (new[] { typeof(MediatorBase).Assembly, typeof(AllocationTests).Assembly }
            .Any(a => a.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true))
        {
            Assert.Inconclusive("Allocations are only meaningful in an optimised build: dotnet test -c Release.");
        }
    }
}
