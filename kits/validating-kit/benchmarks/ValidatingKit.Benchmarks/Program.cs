using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using ValidatingKit.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Samples).Assembly).Run(args);

namespace ValidatingKit.Benchmarks
{
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class ValidationBenchmarks
    {
        private static readonly DeleteFileFluent s_deleteFluent = new();
        private static readonly PlaceOrderFluent s_orderFluent = new();

        [Benchmark(Baseline = true), BenchmarkCategory("Simple, valid")]
        public object? Simple_valid_by_hand() => ByHand.Validate(Samples.ValidDelete);

        [Benchmark, BenchmarkCategory("Simple, valid")]
        public object Simple_valid_ValidatingKit() => Kit.Validate(Samples.ValidDelete);

        [Benchmark, BenchmarkCategory("Simple, valid")]
        public object Simple_valid_FluentValidation() => s_deleteFluent.Validate(Samples.ValidDelete);

        [Benchmark(Baseline = true), BenchmarkCategory("Simple, invalid")]
        public object? Simple_invalid_by_hand() => ByHand.Validate(Samples.InvalidDelete);

        [Benchmark, BenchmarkCategory("Simple, invalid")]
        public object Simple_invalid_ValidatingKit() => Kit.Validate(Samples.InvalidDelete);

        [Benchmark, BenchmarkCategory("Simple, invalid")]
        public object Simple_invalid_FluentValidation() => s_deleteFluent.Validate(Samples.InvalidDelete);

        [Benchmark(Baseline = true), BenchmarkCategory("Order, valid")]
        public object Order_valid_ValidatingKit() => Kit.Validate(Samples.ValidOrder);

        [Benchmark, BenchmarkCategory("Order, valid")]
        public object Order_valid_FluentValidation() => s_orderFluent.Validate(Samples.ValidOrder);

        [Benchmark(Baseline = true), BenchmarkCategory("Order, invalid")]
        public object Order_invalid_ValidatingKit() => Kit.Validate(Samples.InvalidOrder);

        [Benchmark, BenchmarkCategory("Order, invalid")]
        public object Order_invalid_FluentValidation() => s_orderFluent.Validate(Samples.InvalidOrder);
    }
}
