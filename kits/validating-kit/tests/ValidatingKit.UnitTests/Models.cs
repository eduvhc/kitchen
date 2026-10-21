namespace ValidatingKit.UnitTests;

public sealed record DeleteFileRequested(string? StorageId);

public enum Status
{
    Draft,
    Placed,
}

[Flags]
public enum Access
{
    None = 0,
    Read = 1,
    Write = 2,
}

public sealed record Address(string? Street, string? PostalCode);

public sealed record Line(string? Sku, int Quantity);

public sealed record Order
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public Address? Address { get; init; }
    public List<Line>? Lines { get; init; }
    public string[]? Tags { get; init; }
    public DateOnly Start { get; init; }
    public DateOnly End { get; init; }
    public bool Ship { get; init; }
    public int? Discount { get; init; }
    public Guid CustomerId { get; init; }
    public Status Status { get; init; }
}

public sealed class AddressValidator() : Validator<Address>("address.invalid")
{
    public override Validation<Address> Apply(Validation<Address> validation) => validation
        .Field(a => a.Street, f => f.NotBlank())
        .Field(a => a.PostalCode, f => f.NotBlank().MaxLength(8));
}

public sealed class LineValidator() : Validator<Line>("line.invalid")
{
    public override Validation<Line> Apply(Validation<Line> validation) => validation
        .Field(l => l.Sku, f => f.NotBlank())
        .Field(l => l.Quantity, f => f.GreaterThan(0));
}
