using System.Diagnostics.Metrics;
using MessagingKit.Inbox;
using MessagingKit.Inbox.Persistence;
using MessagingKit.Outbox;
using MessagingKit.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MessagingKit.UnitTests;

/// <summary>
/// When the loops count the backlog, and what collection sees in between. The counts come from a fake,
/// so these run without a database; the integration suite runs the real query.
/// </summary>
[TestClass]
public class BacklogSamplingTests
{
    [TestMethod]
    public async Task Collection_reads_the_last_sample_without_counting_again()
    {
        using var fixture = new OutboxFixture();
        using var backlog = fixture.Collect();
        fixture.Counts.Next = (5, 2);

        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);
        fixture.Counts.Next = (50, 20);

        Assert.AreEqual((5L, 2L), Observe(backlog));
        Assert.AreEqual((5L, 2L), Observe(backlog));
        Assert.AreEqual(1, fixture.Counts.Calls);
    }

    [TestMethod]
    public async Task Samples_at_most_once_per_interval()
    {
        using var fixture = new OutboxFixture(intervalSeconds: 30);
        using var backlog = fixture.Collect();

        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(29));
        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);
        Assert.AreEqual(1, fixture.Counts.Calls);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);
        Assert.AreEqual(2, fixture.Counts.Calls);
    }

    [TestMethod]
    public async Task A_failed_count_keeps_the_last_value_and_does_not_throw()
    {
        using var fixture = new OutboxFixture(intervalSeconds: 30);
        using var backlog = fixture.Collect();
        fixture.Counts.Next = (7, 1);
        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);

        fixture.Counts.Failure = new InvalidOperationException("database unreachable");
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);

        Assert.AreEqual(2, fixture.Counts.Calls);
        Assert.AreEqual((7L, 1L), Observe(backlog));
    }

    [TestMethod]
    public async Task Does_not_count_while_nothing_listens()
    {
        using var fixture = new OutboxFixture();

        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);

        Assert.AreEqual(0, fixture.Counts.Calls);
    }

    [TestMethod]
    public async Task A_zero_interval_turns_sampling_off()
    {
        using var fixture = new OutboxFixture(intervalSeconds: 0);
        using var backlog = fixture.Collect();

        await fixture.Dispatcher.SampleBacklogIfDueAsync(CancellationToken.None);

        Assert.AreEqual(0, fixture.Counts.Calls);
        backlog.RecordObservableInstruments();
        Assert.IsEmpty(backlog.GetMeasurementSnapshot());
    }

    [TestMethod]
    public async Task The_inbox_processor_samples_and_survives_a_failure_the_same_way()
    {
        var counts = new FakeCounts { Next = (9, 4) };
        var clock = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddScoped<IInboxBacklog>(_ => counts);
        using var provider = services.BuildServiceProvider();

        var processor = new InboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new InboxOptions { BacklogSampleIntervalSeconds = 10 }),
            new InboxSignal(),
            NullLogger<InboxProcessor>.Instance,
            new InboxMetrics(provider.GetRequiredService<IMeterFactory>()),
            clock);

        using var backlog = new MetricCollector<long>(
            provider.GetRequiredService<IMeterFactory>(),
            MessagingKitInstrumentation.MeterName,
            "messagingkit.inbox.messages");

        await processor.SampleBacklogIfDueAsync(CancellationToken.None);
        counts.Failure = new InvalidOperationException("database unreachable");
        clock.Advance(TimeSpan.FromSeconds(10));
        await processor.SampleBacklogIfDueAsync(CancellationToken.None);

        Assert.AreEqual(2, counts.Calls);
        Assert.AreEqual((9L, 4L), Observe(backlog));
    }

    private static (long Pending, long Dead) Observe(MetricCollector<long> backlog)
    {
        backlog.Clear();
        backlog.RecordObservableInstruments();
        var byStatus = backlog.GetMeasurementSnapshot()
            .ToDictionary(m => (string)m.Tags["messagingkit.message.status"]!, m => m.Value);

        return (byStatus["pending"], byStatus["dead"]);
    }

    private sealed class FakeCounts : IOutboxBacklog, IInboxBacklog
    {
        public (long Pending, long Dead) Next { get; set; }

        public Exception? Failure { get; set; }

        public int Calls { get; private set; }

        public Task<(long Pending, long Dead)> CountAsync(CancellationToken ct)
        {
            Calls++;
            return Failure is null ? Task.FromResult(Next) : Task.FromException<(long, long)>(Failure);
        }
    }

    private sealed class OutboxFixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public OutboxFixture(int intervalSeconds = 30)
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            services.AddScoped<IOutboxBacklog>(_ => Counts);
            _provider = services.BuildServiceProvider();

            Dispatcher = new OutboxDispatcher(
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new OutboxOptions { BacklogSampleIntervalSeconds = intervalSeconds }),
                new OutboxSignal(),
                NullLogger<OutboxDispatcher>.Instance,
                new OutboxMetrics(_provider.GetRequiredService<IMeterFactory>()),
                Clock);
        }

        public FakeCounts Counts { get; } = new();

        public FakeTimeProvider Clock { get; } = new();

        public OutboxDispatcher Dispatcher { get; }

        public MetricCollector<long> Collect() =>
            new(_provider.GetRequiredService<IMeterFactory>(), MessagingKitInstrumentation.MeterName, "messagingkit.outbox.messages");

        public void Dispose()
        {
            Dispatcher.Dispose();
            _provider.Dispose();
        }
    }
}
