using FluentValidation;
using ResultingKit;

namespace ValidatingKit.Benchmarks;

/// <summary>The same rules three ways: ValidatingKit, FluentValidation, and by hand.</summary>
public static class Kit
{
    private static readonly LineRules s_line = new();

    public static Result Validate(DeleteFileRequested? request) =>
        Validation.That(request, "INVALID_REQUEST")
            .Required()
            .Field(r => r.StorageId, f => f.NotBlank())
            .ToResult();

    public static Result Validate(PlaceOrder? order) =>
        Validation.That(order, "order.invalid")
            .Field(o => o.Name, f => f.NotBlank().MaxLength(64))
            .Field(o => o.Email, f => f.NotBlank().Email())
            .Field(o => o.CustomerId, f => f.NotEmpty())
            .Field(o => o.Discount, f => f.Between(0, 100))
            .Field(o => o.Address, f => f.Required().Valid(a => a
                .Field(x => x.Street, s => s.NotBlank())
                .Field(x => x.PostalCode, p => p.NotBlank().MaxLength(10))))
            .Field(o => o.Lines, f => f.NotEmpty().MaxLength(50).Each(l => l.Required().Valid(s_line)))
            .ToResult();

    private sealed class LineRules() : Validator<Line>("line.invalid")
    {
        public override Validation<Line> Apply(Validation<Line> line) => line
            .Field(l => l.Sku, f => f.NotBlank())
            .Field(l => l.Quantity, f => f.GreaterThan(0));
    }
}

public sealed class DeleteFileFluent : AbstractValidator<DeleteFileRequested>
{
    public DeleteFileFluent()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(r => r.StorageId).NotEmpty();
    }
}

public sealed class AddressFluent : AbstractValidator<Address>
{
    public AddressFluent()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(a => a.Street).NotEmpty();
        RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(10);
    }
}

public sealed class LineFluent : AbstractValidator<Line>
{
    public LineFluent()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(l => l.Sku).NotEmpty();
        RuleFor(l => l.Quantity).GreaterThan(0);
    }
}

public sealed class PlaceOrderFluent : AbstractValidator<PlaceOrder>
{
    public PlaceOrderFluent()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;
        RuleFor(o => o.Name).NotEmpty().MaximumLength(64);
        RuleFor(o => o.Email).NotEmpty().EmailAddress();
        RuleFor(o => o.CustomerId).NotEmpty();
        RuleFor(o => o.Discount).InclusiveBetween(0, 100);
        RuleFor(o => o.Address).NotNull().SetValidator(new AddressFluent()!);
        RuleFor(o => o.Lines).NotEmpty().Must(l => l!.Count <= 50);
        RuleForEach(o => o.Lines).NotNull().SetValidator(new LineFluent());
    }
}

public static class ByHand
{
    public static Error? Validate(DeleteFileRequested? request)
    {
        var errors = new List<string>();
        if (request is null) errors.Add("request is required.");
        else if (string.IsNullOrWhiteSpace(request.StorageId)) errors.Add("storageId is required.");
        return errors.Count > 0 ? Error.Validation("INVALID_REQUEST", string.Join(" ", errors)) : null;
    }
}
