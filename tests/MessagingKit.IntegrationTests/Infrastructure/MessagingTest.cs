using MessagingKit.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using TestingKit;
using TestingKit.MSTest;

namespace MessagingKit.IntegrationTests.Infrastructure;

public abstract class MessagingTest : IntegrationTest
{
    protected override TestEnvironment Environment => TestHost.Environment;

    [TestInitialize]
    public void ResetDoubles()
    {
        TestHost.Transport.Reset();
        TestHost.Handlers.Reset();
    }

    protected static async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = TestHost.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    protected static async Task WithScopeAsync(Func<IServiceProvider, Task> action) =>
        await WithScopeAsync<object?>(async services =>
        {
            await action(services);
            return null;
        });

    protected static Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<TestDbContext>()));

    protected static Task WithDbAsync(Func<TestDbContext, Task> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<TestDbContext>()));
}
