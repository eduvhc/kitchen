using System.Diagnostics;
using System.Diagnostics.Metrics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using KitPing = MediatingKit.Benchmarks.Kit.Ping;
using KitPinged = MediatingKit.Benchmarks.Kit.Pinged;
using MediatorPing = MediatingKit.Benchmarks.Mediator.Ping;
using MediatorPinged = MediatingKit.Benchmarks.Mediator.Pinged;
using MediatRPing = MediatingKit.Benchmarks.MediatR.Ping;
using MediatRPinged = MediatingKit.Benchmarks.MediatR.Pinged;

namespace MediatingKit.Benchmarks;

/// <summary>
/// Each library's mediator is resolved once, as an application holds it, and called through its interface. Every
/// library runs with its own default handler lifetime: MediatingKit scoped (resolved from one scope), MediatR transient,
/// Mediator singleton.
/// </summary>
public abstract class MediatorBenchmarks
{
    private readonly List<IDisposable> _disposables = [];

    protected MediatingKit.IMediator KitMediator { get; private set; } = null!;

    protected global::MediatR.IMediator MediatRMediator { get; private set; } = null!;

    protected global::Mediator.IMediator SourceGeneratedMediator { get; private set; } = null!;

    protected IServiceProvider KitServices { get; private set; } = null!;

    protected IServiceProvider MediatRServices { get; private set; } = null!;

    protected IServiceProvider SourceGeneratedServices { get; private set; } = null!;

    protected abstract bool WithBehavior { get; }

    [GlobalSetup]
    public void Setup()
    {
        Build();
        AfterSetup();
    }

    protected virtual void AfterSetup()
    {
    }

    private void Build()
    {
        // Mediator's generator reads any AddMediator call with a lambda as its own configuration, so the options are
        // passed as a variable; the call is otherwise the one-liner an application writes.
        Action<MediatorOptions> configure = WithBehavior ? o => o.AddBehavior(typeof(Kit.PassThrough<,>)) : _ => { };
        var kit = new ServiceCollection();
        MediatingKit.MediatingServiceCollectionExtensions.AddMediator(kit, configure);
        KitMediator = Resolve<MediatingKit.IMediator>(kit, out var kitServices);
        KitServices = kitServices;

        if (WithBehavior != kit.Any(d => d.ServiceType == typeof(MediatingKit.IPipelineBehavior<,>)))
        {
            throw new InvalidOperationException("MediatingKit's behavior registration does not match the benchmark's parameter.");
        }

        var mediatR = new ServiceCollection().AddLogging();
        mediatR.AddMediatR(o =>
        {
            o.RegisterServicesFromAssemblyContaining<MediatRPing>();

            if (WithBehavior)
            {
                o.AddOpenBehavior(typeof(MediatR.PassThrough<,>));
            }
        });
        MediatRMediator = Resolve<global::MediatR.IMediator>(mediatR, out var mediatRServices);
        MediatRServices = mediatRServices;

        var mediator = new ServiceCollection();
        global::SourceGeneratedMediator.SourceGeneratedMediatorSetup.Add(mediator);

        if (WithBehavior)
        {
            mediator.AddSingleton(typeof(global::Mediator.IPipelineBehavior<,>), typeof(Mediator.PassThrough<,>));
        }

        SourceGeneratedMediator = Resolve<global::Mediator.IMediator>(mediator, out var sourceGeneratedServices);
        SourceGeneratedServices = sourceGeneratedServices;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    protected void Track(IDisposable disposable) => _disposables.Add(disposable);

    private T Resolve<T>(IServiceCollection services, out IServiceProvider root)
        where T : notnull
    {
        var provider = services.BuildServiceProvider();
        root = provider;
        var scope = provider.CreateScope();
        _disposables.Add(provider);
        _disposables.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SendBenchmarks : MediatorBenchmarks
{
    private static readonly KitPing KitRequest = new(1);
    private static readonly MediatRPing MediatRRequest = new(1);
    private static readonly MediatorPing MediatorRequest = new(1);

    private readonly Kit.PingHandler _handler = new();

    [Params(false, true)]
    public bool Behavior { get; set; }

    protected override bool WithBehavior => Behavior;

    [Benchmark(Baseline = true, Description = "Direct handler call")]
    public ValueTask<Kit.Pong> Direct() => _handler.Handle(KitRequest, CancellationToken.None);

    [Benchmark(Description = "MediatingKit")]
    public ValueTask<Kit.Pong> MediatingKitSend() => KitMediator.Send(KitRequest, CancellationToken.None);

    [Benchmark(Description = "Mediator 3.0.2")]
    public ValueTask<Mediator.Pong> MediatorSend() => SourceGeneratedMediator.Send(MediatorRequest, CancellationToken.None);

    [Benchmark(Description = "MediatR 14.2.0")]
    public Task<MediatR.Pong> MediatRSend() => MediatRMediator.Send(MediatRRequest, CancellationToken.None);
}

[MemoryDiagnoser]
public class PublishBenchmarks : MediatorBenchmarks
{
    private static readonly KitPinged KitNotification = new(1);
    private static readonly MediatRPinged MediatRNotification = new(1);
    private static readonly MediatorPinged MediatorNotification = new(1);

    private readonly Kit.FirstPingedHandler _first = new();
    private readonly Kit.SecondPingedHandler _second = new();

    protected override bool WithBehavior => false;

    [Benchmark(Baseline = true, Description = "Direct handler calls")]
    public async ValueTask Direct()
    {
        await _first.Handle(KitNotification, CancellationToken.None);
        await _second.Handle(KitNotification, CancellationToken.None);
    }

    [Benchmark(Description = "MediatingKit")]
    public ValueTask MediatingKitPublish() => KitMediator.Publish(KitNotification, CancellationToken.None);

    [Benchmark(Description = "Mediator 3.0.2")]
    public ValueTask MediatorPublish() => SourceGeneratedMediator.Publish(MediatorNotification, CancellationToken.None);

    [Benchmark(Description = "MediatR 14.2.0")]
    public Task MediatRPublish() => MediatRMediator.Publish(MediatRNotification, CancellationToken.None);
}

/// <summary>
/// What MediatingKit's instrumentation costs. With nobody listening it is one check per call; with a tracer sampling
/// every span and a meter listener attached it is the price of a span and two measurements.
/// </summary>
[MemoryDiagnoser]
public class TelemetryBenchmarks : MediatorBenchmarks
{
    private static readonly KitPing KitRequest = new(1);

    [Params(false, true)]
    public bool Listening { get; set; }

    protected override bool WithBehavior => false;

    protected override void AfterSetup()
    {
        if (!Listening)
        {
            return;
        }

        var tracer = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MediatingKitInstrumentation.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(tracer);
        Track(tracer);

        var meter = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == MediatingKitInstrumentation.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meter.Start();
        Track(meter);
    }

    [Benchmark(Description = "MediatingKit send")]
    public ValueTask<Kit.Pong> MediatingKitSend() => KitMediator.Send(KitRequest, CancellationToken.None);
}

/// <summary>
/// One send from a fresh scope, as an ASP.NET Core request makes it: create the scope, resolve the mediator, send,
/// dispose. This is where MediatingKit's scoped default pays for a mediator and a handler per scope, and the container
/// for tracking them; with singleton lifetime it does what Mediator does here. MediatR resolves per send regardless.
/// </summary>
[MemoryDiagnoser]
public class ScopedSendBenchmarks : MediatorBenchmarks
{
    private static readonly KitPing KitRequest = new(1);
    private static readonly MediatRPing MediatRRequest = new(1);
    private static readonly MediatorPing MediatorRequest = new(1);

    private IServiceProvider _singletonKit = null!;

    protected override bool WithBehavior => false;

    protected override void AfterSetup()
    {
        Action<MediatorOptions> configure = o => o.Lifetime = ServiceLifetime.Singleton;
        var services = new ServiceCollection();
        MediatingKit.MediatingServiceCollectionExtensions.AddMediator(services, configure);
        var provider = services.BuildServiceProvider();
        Track(provider);
        _singletonKit = provider;
    }

    [Benchmark(Baseline = true, Description = "MediatingKit")]
    public async ValueTask<Kit.Pong> MediatingKitSend()
    {
        await using var scope = KitServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediatingKit.IMediator>().Send(KitRequest, CancellationToken.None);
    }

    [Benchmark(Description = "MediatingKit, singleton")]
    public async ValueTask<Kit.Pong> MediatingKitSingletonSend()
    {
        await using var scope = _singletonKit.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MediatingKit.IMediator>().Send(KitRequest, CancellationToken.None);
    }

    [Benchmark(Description = "Mediator 3.0.2")]
    public async ValueTask<Mediator.Pong> MediatorSend()
    {
        await using var scope = SourceGeneratedServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<global::Mediator.IMediator>().Send(MediatorRequest, CancellationToken.None);
    }

    [Benchmark(Description = "MediatR 14.2.0")]
    public async Task<MediatR.Pong> MediatRSend()
    {
        await using var scope = MediatRServices.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<global::MediatR.IMediator>().Send(MediatRRequest, CancellationToken.None);
    }
}
