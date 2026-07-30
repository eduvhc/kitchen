using EmailService.Features.Emails;
using Microsoft.Extensions.DependencyInjection;
using TestingKit;
using TestingKit.MSTest;

namespace EmailService.IntegrationTests;

public abstract class ApiTest : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    protected static EmailServiceFactory Factory => TestHost.Factory;

    protected static async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected static Task<T> WithQueueAsync<T>(Func<IEmailQueue, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<IEmailQueue>()));

    protected static async Task WithQueueAsync(Func<IEmailQueue, Task> action) =>
        await WithScopeAsync<object?>(async services =>
        {
            await action(services.GetRequiredService<IEmailQueue>());
            return null;
        });

    protected static EmailMessage NewMessage(
        string to = "ada@example.com",
        DateTimeOffset? scheduledAt = null,
        int maxAttempts = 5,
        string? idempotencyKey = null)
    {
        var now = Factory.Clock.GetUtcNow();

        return new EmailMessage
        {
            FromAddress = "no-reply@example.com",
            FromName = "Example",
            To = [to],
            Subject = "Integration test",
            HtmlBody = "<p>Integration test</p>",
            MaxAttempts = maxAttempts,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt ?? now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
