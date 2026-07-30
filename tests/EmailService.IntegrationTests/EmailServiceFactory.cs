using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TestingKit;
using TestingKit.AspNetCore;

namespace EmailService.IntegrationTests;

public class EmailServiceFactory : TestingKitWebApplicationFactory<Program>
{
    public EmailServiceFactory(TestEnvironment environment)
        : base(environment) =>
        WithServices(services => services.AddSingleton<TimeProvider>(Clock));

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public HttpClient CreateAdminClient() => CreateClientWithKey(TestHost.AdminApiKey);

    public HttpClient CreateSenderClient() => CreateClientWithKey(TestHost.SenderApiKey);

    public HttpClient CreateClientWithKey(string? apiKey)
    {
        var client = CreateClient();

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
