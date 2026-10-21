using System.Diagnostics;
using System.Reflection;

namespace ValidatingKit.UnitTests;

[TestClass]
public sealed class AllocationTests
{
    private static readonly LineValidator s_line = new();

    private static Result Validate(Order order) =>
        Validation.That(order, "order.invalid")
            .Field(o => o.Name, f => f.NotBlank().MaxLength(64))
            .Field(o => o.Email, f => f.NotBlank().Email())
            .Field(o => o.CustomerId, f => f.NotEmpty())
            .Field(o => o.Status, f => f.IsInEnum())
            .Field(o => o.Discount, f => f.Between(0, 100))
            .Field(o => o.End, (f, o) => f.GreaterThanOrEqualTo(o.Start))
            .Field(o => o.Lines, f => f.NotEmpty().MaxLength(10).Each(l => l.Valid(s_line)))
            .ToResult();

    [TestMethod]
    [DoNotParallelize]
    public void A_valid_value_allocates_only_the_failure_collector()
    {
        if (new[] { typeof(Validation).Assembly, typeof(AllocationTests).Assembly }
            .Any(a => a.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true))
        {
            Assert.Inconclusive("Allocations are only meaningful in an optimised build: dotnet test -c Release.");
        }

        var order = new Order
        {
            Name = "Tea",
            Email = "a@b.c",
            CustomerId = Guid.NewGuid(),
            Discount = 5,
            Lines = [new Line("A", 1), new Line("B", 2)],
        };

        Assert.IsTrue(Validate(order).IsSuccess);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Validate(order);
        }

        var perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 100;

        Assert.IsLessThanOrEqualTo(40L, perCall, $"{perCall} bytes per validation.");
    }
}
