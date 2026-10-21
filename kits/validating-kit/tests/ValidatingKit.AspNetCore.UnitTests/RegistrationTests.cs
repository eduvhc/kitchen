using Microsoft.Extensions.DependencyInjection;

namespace ValidatingKit.AspNetCore.UnitTests;

public sealed record Order(string? Name);

public sealed record Note(string? Text);

public sealed class OrderValidator : Validator<Order>
{
    public override Validation<Order> Apply(Validation<Order> validation) => validation.Field(o => o.Name, f => f.NotBlank());
}

public sealed class SharedValidator : IValidator<Order>, IValidator<Note>
{
    public Validation<Order> Apply(Validation<Order> validation) => validation;

    public Validation<Note> Apply(Validation<Note> validation) => validation;

    public ResultingKit.Result<Order> Validate(Order? value) => Validation.That(value).ToValueResult();

    public ResultingKit.Result<Note> Validate(Note? value) => Validation.That(value).ToValueResult();
}

public abstract class AbstractOrderValidator : Validator<Order>;

public sealed class GenericValidator<T> : Validator<T>
{
    public override Validation<T> Apply(Validation<T> validation) => validation;
}

[TestClass]
public sealed class RegistrationTests
{
    [TestMethod]
    public void Add_validator_registers_a_singleton_for_its_contract()
    {
        using var provider = new ServiceCollection().AddValidator<OrderValidator>().BuildServiceProvider();

        var validator = provider.GetRequiredService<IValidator<Order>>();

        Assert.IsInstanceOfType<OrderValidator>(validator);
        Assert.AreSame(validator, provider.GetRequiredService<IValidator<Order>>());
        Assert.AreSame(validator, provider.GetRequiredService<OrderValidator>());
    }

    [TestMethod]
    public void A_validator_for_several_types_is_one_instance()
    {
        using var provider = new ServiceCollection().AddValidator<SharedValidator>().BuildServiceProvider();

        Assert.AreSame<object>(provider.GetRequiredService<IValidator<Order>>(), provider.GetRequiredService<IValidator<Note>>());
    }

    [TestMethod]
    public void An_existing_registration_wins()
    {
        var services = new ServiceCollection().AddSingleton<IValidator<Order>, SharedValidator>().AddValidator<OrderValidator>();
        using var provider = services.BuildServiceProvider();

        Assert.IsInstanceOfType<SharedValidator>(provider.GetRequiredService<IValidator<Order>>());
    }

    [TestMethod]
    public void A_type_that_is_not_a_validator_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddValidator<Order>());
    }

    [TestMethod]
    public void Scanning_registers_concrete_validators_only()
    {
        var services = new ServiceCollection().AddValidatorsFromAssemblyContaining<RegistrationTests>();

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(OrderValidator)));
        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(SharedValidator)));
        Assert.IsFalse(services.Any(d => d.ServiceType == typeof(AbstractOrderValidator)));
        Assert.IsFalse(services.Any(d => d.ServiceType.IsGenericTypeDefinition));
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IValidator<Order>)));
        Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(IValidator<Note>)));
    }

    [TestMethod]
    public void Null_arguments_are_rejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ((IServiceCollection)null!).AddValidator<OrderValidator>());
        Assert.ThrowsExactly<ArgumentNullException>(() => new ServiceCollection().AddValidatorsFromAssembly(null!));
    }
}
