namespace ValidatingKit.Benchmarks;

public sealed record DeleteFileRequested(string? StorageId);

public sealed record Address(string? Street, string? PostalCode);

public sealed record Line(string? Sku, int Quantity);

public sealed record PlaceOrder(
    string? Name,
    string? Email,
    Guid CustomerId,
    int? Discount,
    Address? Address,
    List<Line>? Lines);

public static class Samples
{
    public static readonly DeleteFileRequested ValidDelete = new("storage-1");

    public static readonly DeleteFileRequested InvalidDelete = new(" ");

    public static readonly PlaceOrder ValidOrder = new(
        "Tea for two",
        "ana@example.com",
        Guid.Parse("8f14e45f-ceea-467f-a8f4-6e8c1d2b7a90"),
        10,
        new Address("1 Main St", "1000-001"),
        [new Line("SKU-1", 1), new Line("SKU-2", 3), new Line("SKU-3", 2), new Line("SKU-4", 5), new Line("SKU-5", 1)]);

    public static readonly PlaceOrder InvalidOrder = new(
        "",
        "not-an-email",
        Guid.Empty,
        150,
        new Address("", "1000-001-too-long"),
        [new Line("SKU-1", 1), new Line("", 0), new Line("SKU-3", -2)]);
}
