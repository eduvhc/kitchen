using System.Net.Http.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace TestingKit.Smtp;

public class SmtpFixture(
    SmtpContainerOptions? containerOptions = null,
    SmtpClientOptions? clientOptions = null)
    : TestFixtureBase<SmtpContainerOptions, SmtpClientOptions>(containerOptions, clientOptions), IResettableFixture, IDisposable
{
    private IContainer? _container;
    private HttpClient? _api;

    public string Host { get; private set; } = "localhost";

    public int SmtpPort { get; private set; }

    public Uri ApiBaseAddress { get; private set; } = null!;

    public async Task<IReadOnlyList<CapturedEmail>> GetMessagesAsync(CancellationToken ct = default)
    {
        var response = await Api.GetFromJsonAsync<MailpitMessages>("api/v1/messages", ct);
        return response?.Messages ?? [];
    }

    public async Task<CapturedEmail> WaitForMessageAsync(
        Func<CapturedEmail, bool>? predicate = null,
        CancellationToken ct = default)
    {
        var matches = await WaitForMessagesAsync(1, predicate, ct);
        return matches[0];
    }

    public async Task<IReadOnlyList<CapturedEmail>> WaitForMessagesAsync(
        int count,
        Func<CapturedEmail, bool>? predicate = null,
        CancellationToken ct = default)
    {
        predicate ??= _ => true;
        var deadline = DateTimeOffset.UtcNow + Client.WaitTimeout;
        IReadOnlyList<CapturedEmail> matches = [];

        while (DateTimeOffset.UtcNow < deadline)
        {
            matches = [.. (await GetMessagesAsync(ct)).Where(predicate)];

            if (matches.Count >= count)
            {
                return matches;
            }

            await Task.Delay(Client.PollInterval, ct);
        }

        throw new TimeoutException(
            $"Expected {count} matching message(s) within {Client.WaitTimeout.TotalSeconds:0.#}s but found {matches.Count}.");
    }

    public async Task<CapturedEmailBody> GetBodyAsync(string messageId, CancellationToken ct = default) =>
        await Api.GetFromJsonAsync<CapturedEmailBody>($"api/v1/message/{messageId}", ct)
        ?? throw new InvalidOperationException($"Message '{messageId}' was not found.");

    public async Task ResetAsync(CancellationToken ct = default)
    {
        using var response = await Api.DeleteAsync(new Uri("api/v1/messages", UriKind.Relative), ct);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        _api?.Dispose();
        _api = null;
        GC.SuppressFinalize(this);
    }

    protected HttpClient Api => _api ?? throw new InvalidOperationException(
        $"{nameof(SmtpFixture)} is not ready. Call StartAsync() first.");

    protected override async Task StartContainerAsync(CancellationToken ct)
    {
        var builder = new ContainerBuilder(Container.Image!)
            .WithPortBinding(Container.SmtpPort, assignRandomHostPort: true)
            .WithPortBinding(Container.ApiPort, assignRandomHostPort: true)
            .WithReuse(Container.Reuse)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort((ushort)Container.ApiPort)
                    .ForPath("/readyz")));

        foreach (var (key, value) in Container.Labels)
        {
            builder = builder.WithLabel(key, value);
        }

        _container = builder.Build();
        await _container.StartAsync(ct);

        Host = _container.Hostname;
        SmtpPort = _container.GetMappedPublicPort(Container.SmtpPort);
        ApiBaseAddress = new Uri($"http://{Host}:{_container.GetMappedPublicPort(Container.ApiPort)}/");
        ConnectionString = $"smtp://{Host}:{SmtpPort}";
    }

    protected override async ValueTask DisposeContainerAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    protected override Task OnExternalConnectedAsync(CancellationToken ct)
    {
        var uri = new Uri(ConnectionString);
        Host = uri.Host;
        SmtpPort = uri.Port;
        ApiBaseAddress = new Uri(Client.ApiBaseAddress
            ?? throw new InvalidOperationException(
                $"{nameof(SmtpClientOptions)}.{nameof(SmtpClientOptions.ApiBaseAddress)} is required when using an external SMTP server."));

        return Task.CompletedTask;
    }

    protected override Task OnAfterStartAsync(CancellationToken ct)
    {
        _api = new HttpClient { BaseAddress = ApiBaseAddress };
        return Task.CompletedTask;
    }

    protected override Task OnBeforeDisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    private sealed record MailpitMessages
    {
        [JsonPropertyName("messages")]
        public IReadOnlyList<CapturedEmail> Messages { get; init; } = [];
    }
}
