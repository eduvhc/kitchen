using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestingKit.MSTest;

public abstract class IntegrationTest
{
    public TestContext TestContext { get; set; } = null!;

    protected abstract TestEnvironment Environment { get; }

    protected CancellationToken CancellationToken => TestContext?.CancellationToken ?? CancellationToken.None;

    [TestInitialize]
    public virtual Task ResetStateAsync() => Environment.ResetAsync(CancellationToken);
}
