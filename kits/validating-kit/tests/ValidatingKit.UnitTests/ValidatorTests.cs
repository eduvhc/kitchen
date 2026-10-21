namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class ValidatorTests
{
    private sealed class Empty() : Validator<Order>("")
    {
        public override Validation<Order> Apply(Validation<Order> validation) => validation;
    }

    [TestMethod]
    public void Validate_returns_the_value_on_success()
    {
        var address = new Address("Main", "1000");

        var result = new AddressValidator().Validate(address);

        Assert.AreSame(address, result.Value);
    }

    [TestMethod]
    public void Validate_fails_with_the_validator_code()
    {
        var error = new AddressValidator().Validate(new Address("", "1")).Error!;

        Assert.AreEqual("address.invalid", error.Code);
        Assert.AreEqual(ErrorKind.Validation, error.Kind);
        Assert.AreEqual("street: Must not be blank.", error.Message);
    }

    [TestMethod]
    public void Validate_names_a_null_value_after_its_type()
    {
        Assert.AreEqual("address: Is required.", new AddressValidator().Validate(null).Error!.Message);
    }

    [TestMethod]
    public void A_validator_is_usable_through_its_interface()
    {
        static string Check(IValidator<Line> validator) => validator.Validate(new Line("A", 0)).Error!.Message;

        Assert.AreEqual("quantity: Must be greater than 0.", Check(new LineValidator()));
    }

    private sealed class Defaulted : Validator<Order>
    {
        public override Validation<Order> Apply(Validation<Order> validation) => validation.Field(o => o.Name, f => f.NotBlank());
    }

    [TestMethod]
    public void Code_defaults_to_validation_failed()
    {
        var validator = new Defaulted();

        Assert.AreEqual(Validation.DefaultCode, validator.Code);
        Assert.AreEqual(Validation.DefaultCode, validator.Validate(new Order()).Error!.Code);
    }

    [TestMethod]
    public void Code_cannot_be_empty()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Empty());
    }
}
