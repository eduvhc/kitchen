using Microsoft.Extensions.DependencyInjection;

namespace ValidatingKit.AspNetCore.UnitTests;

[TestClass]
public sealed class ArgumentCheckTests
{
    private static void Handler(Order order, Note note, int id, CancellationToken ct)
    {
    }

    private static ArgumentCheck[] Checks(IServiceProvider services) =>
        ArgumentCheck.ForValidators(ArgumentCheck.Describe(((Action<Order, Note, int, CancellationToken>)Handler).Method), services);

    [TestMethod]
    public void Only_parameters_with_a_registered_validator_are_checked()
    {
        using var provider = new ServiceCollection().AddValidator<OrderValidator>().BuildServiceProvider();

        var check = Checks(provider).Single();

        Assert.AreEqual(0, check.Index);
        Assert.AreEqual("order", check.Name);
    }

    [TestMethod]
    public void A_registered_validator_runs_against_the_argument()
    {
        using var provider = new ServiceCollection().AddValidator<OrderValidator>().BuildServiceProvider();
        var check = Checks(provider).Single();

        Assert.AreSame(ResultingKit.Result.Success(), check.Validate(provider, new Order("Tea")));
        Assert.AreEqual("name: Must not be blank.", check.Validate(provider, new Order("")).Error!.Message);
        Assert.AreEqual("order: Is required.", check.Validate(provider, null).Error!.Message);
    }

    [TestMethod]
    public void A_validator_that_is_not_a_validator_base_runs_through_its_interface()
    {
        using var provider = new ServiceCollection().AddValidator<SharedValidator>().BuildServiceProvider();

        Assert.AreEqual(2, Checks(provider).Length);
        Assert.IsTrue(Checks(provider)[1].Validate(provider, null).IsFailure);
    }

    [TestMethod]
    public void Without_service_lookup_every_parameter_is_checked_and_missing_validators_pass()
    {
        var services = new NoLookupProvider();

        var checks = Checks(services);

        Assert.AreEqual(4, checks.Length);
        Assert.IsTrue(checks.All(c => c.Validate(services, null).IsSuccess));
    }

    private sealed class NoLookupProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
