using System.Diagnostics.Metrics;
using MessagingKit.Abstractions;
using MessagingKit.Inbox;
using MessagingKit.IntegrationTests.Infrastructure;
using MessagingKit.Outbox;
using MessagingKit.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace MessagingKit.IntegrationTests;

/// <summary>
/// The backlog instruments run the real loops against the real tables. Each test gets its own host,
/// clock and meter factory, so the sample schedule and cached counts start fresh.
/// </summary>
[TestClass]
public class BacklogMetricsTests : MessagingTest
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    [TestMethod]
    public async Task The_dispatcher_loop_samples_pending_and_dead_outbox_rows()
    {
        await using var host = new LoopHost();
        await SeedOutboxAsync(host.Clock, pending: 3, dead: 2);
        using var backlog = host.Collect("messagingkit.outbox.messages");

        await host.RunUntilAsync<OutboxDispatcher>(() => Counts(backlog) == (3, 2));

        Assert.AreEqual("{message}", backlog.Instrument!.Unit);
        Assert.IsTrue(backlog.GetMeasurementSnapshot().All(m => Equals(m.Tags["messaging.system"], "messagingkit")));
    }

    [TestMethod]
    public async Task The_processor_loop_samples_pending_and_dead_inbox_rows()
    {
        await using var host = new LoopHost();
        await SeedInboxAsync(host.Clock, pending: 4, dead: 1);
        using var backlog = host.Collect("messagingkit.inbox.messages");

        await host.RunUntilAsync<InboxProcessor>(() => Counts(backlog) == (4, 1));
    }

    [TestMethod]
    public async Task Collection_reports_the_cached_sample_until_the_next_one_is_due()
    {
        await using var host = new LoopHost();
        await SeedOutboxAsync(host.Clock, pending: 1, dead: 0);
        using var backlog = host.Collect("messagingkit.outbox.messages");

        await host.RunUntilAsync<OutboxDispatcher>(() => Counts(backlog) == (1, 0));

        // The loop keeps polling every second, but the fake clock has not reached the next sample, so
        // collection must keep reading the cached count rather than the table.
        await SeedOutboxAsync(host.Clock, pending: 2, dead: 3);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.AreEqual((1L, 0L), Counts(backlog));

        host.Clock.Advance(TimeSpan.FromSeconds(LoopHost.SampleIntervalSeconds));
        await host.RunUntilAsync<OutboxDispatcher>(() => Counts(backlog) == (3, 3));
    }

    [TestMethod]
    public async Task Nothing_is_reported_before_the_first_sample()
    {
        await using var host = new LoopHost();
        using var backlog = host.Collect("messagingkit.inbox.messages");

        backlog.RecordObservableInstruments();

        Assert.IsEmpty(backlog.GetMeasurementSnapshot());
    }

    private static (long Pending, long Dead) Counts(MetricCollector<long> backlog)
    {
        backlog.RecordObservableInstruments();
        var latest = backlog.GetMeasurementSnapshot()
            .GroupBy(m => (string)m.Tags["messagingkit.message.status"]!)
            .ToDictionary(g => g.Key, g => g.Last().Value);

        return (latest.GetValueOrDefault("pending", -1), latest.GetValueOrDefault("dead", -1));
    }

    // Scheduled a day out, so the running loop counts them without ever claiming them.
    private static Task SeedOutboxAsync(TimeProvider clock, int pending, int dead) =>
        WithDbAsync(async db =>
        {
            var later = clock.GetUtcNow().AddDays(1);

            for (var i = 0; i < pending + dead; i++)
            {
                db.Set<OutboxMessage>().Add(new OutboxMessage
                {
                    Type = "send-email",
                    Payload = "{}",
                    Status = i < pending ? OutboxStatus.Pending : OutboxStatus.Dead,
                    MaxAttempts = 10,
                    ScheduledAt = later,
                    CreatedAt = later,
                    UpdatedAt = later,
                });
            }

            await db.SaveChangesAsync();
        });

    private static Task SeedInboxAsync(TimeProvider clock, int pending, int dead) =>
        WithDbAsync(async db =>
        {
            var later = clock.GetUtcNow().AddDays(1);

            for (var i = 0; i < pending + dead; i++)
            {
                db.Set<InboxMessage>().Add(new InboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    Type = "send-email",
                    Payload = "{}",
                    Status = i < pending ? InboxStatus.Pending : InboxStatus.Dead,
                    MaxAttempts = 10,
                    ScheduledAt = later,
                    ReceivedAt = later,
                    UpdatedAt = later,
                });
            }

            await db.SaveChangesAsync();
        });

    private sealed class LoopHost : IAsyncDisposable
    {
        public const int SampleIntervalSeconds = 60;

        private readonly ServiceProvider _services;
        private readonly List<IHostedService> _started = [];

        public LoopHost()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(TestHost.Handlers);
            services.AddScoped<IMessageTransport>(_ => TestHost.Transport);
            services.AddDbContext<TestDbContext>(b => b.UseNpgsql(TestHost.Postgres.ConnectionString));

            services.AddOutbox<TestDbContext>().AddMessage<SendEmail>("send-email");
            services.AddInbox<TestDbContext>().AddHandler<SendEmail, RecordingHandler>("send-email");
            services.Configure<OutboxOptions>(o =>
            {
                o.PollIntervalSeconds = 1;
                o.BacklogSampleIntervalSeconds = SampleIntervalSeconds;
            });
            services.Configure<InboxOptions>(o =>
            {
                o.PollIntervalSeconds = 1;
                o.BacklogSampleIntervalSeconds = SampleIntervalSeconds;
            });

            _services = services.BuildServiceProvider();
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

        public MetricCollector<long> Collect(string instrument) =>
            new(_services.GetRequiredService<IMeterFactory>(), MessagingKitInstrumentation.MeterName, instrument);

        public async Task RunUntilAsync<TLoop>(Func<bool> condition)
            where TLoop : IHostedService
        {
            var loop = _services.GetRequiredService<TLoop>();

            if (!_started.Contains(loop))
            {
                await loop.StartAsync(CancellationToken.None);
                _started.Add(loop);
            }

            using var timeout = new CancellationTokenSource(Patience);

            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var loop in _started)
            {
                await loop.StopAsync(CancellationToken.None);
            }

            await _services.DisposeAsync();
        }
    }
}
