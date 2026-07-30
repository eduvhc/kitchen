using EmailService.Features.Emails.SendEmail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using System.Net.Http.Headers;
using TestingKit.AspNetCore;
using TestingKit;

namespace EmailService.IntegrationTests.Infrastructure;

public class EmailServiceFactory : TestingKitWebApplicationFactory<Program>
{
    public EmailServiceFactory(TestEnvironment environment)
        : base(environment) =>
        WithServices(services => services.AddSingleton<TimeProvider>(Clock));

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));

    public HttpClient CreateApiClient(string? source = "tests")
    {
        var client = CreateClient();

        if (source is not null)
        {
            client.DefaultRequestHeaders.Add(SendEmailEndpoint.SourceHeader, source);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
