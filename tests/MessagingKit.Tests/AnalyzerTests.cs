using System.Collections.Immutable;
using MessagingKit.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MessagingKit.Tests;

/// <summary>
/// Compiles snippets in memory and runs the analyzer over them, so the rules are covered without
/// a build-time probe.
/// </summary>
[TestClass]
public class AnalyzerTests
{
    [TestMethod]
    public async Task Flags_two_message_types_sharing_a_wire_name()
    {
        var diagnostics = await AnalyzeAsync("""
            using MessagingKit;

            [Message("duplicate")]
            public sealed record First(string Value);

            [Message("duplicate")]
            public sealed record Second(string Value);
            """);

        var reported = diagnostics.Where(d => d.Id == "MK1001").ToList();

        Assert.HasCount(2, reported, "both declarations should be flagged");
        StringAssert.Contains(reported[0].GetMessage(), "duplicate");
    }

    [TestMethod]
    public async Task Flags_a_handler_that_is_never_registered()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using MessagingKit;

            public sealed record Orphan(string Value);

            public sealed class OrphanHandler : IMessageHandler<Orphan>
            {
                public Task HandleAsync(Orphan message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }
            """);

        var reported = diagnostics.Single(d => d.Id == "MK1002");

        StringAssert.Contains(reported.GetMessage(), "OrphanHandler");
        StringAssert.Contains(reported.GetMessage(), "Orphan");
    }

    [TestMethod]
    public async Task Accepts_a_handler_that_is_registered()
    {
        var diagnostics = await AnalyzeAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using MessagingKit;
            using Microsoft.Extensions.DependencyInjection;

            public sealed record Welcome(string Value);

            public sealed class WelcomeHandler : IMessageHandler<Welcome>
            {
                public Task HandleAsync(Welcome message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }

            public static class Registration
            {
                public static void Add(IServiceCollection services)
                    => services.AddMessageHandler<Welcome, WelcomeHandler>();
            }
            """);

        Assert.IsEmpty(diagnostics.Where(d => d.Id == "MK1002"));
    }

    [TestMethod]
    public async Task Flags_an_empty_message_name()
    {
        var diagnostics = await AnalyzeAsync("""
            using MessagingKit;

            [Message("")]
            public sealed record Blank(string Value);
            """);

        Assert.HasCount(1, diagnostics.Where(d => d.Id == "MK1003"));
    }

    [TestMethod]
    public async Task Does_not_count_a_routing_call_as_registering_a_handler()
    {
        // UseTransportFor names a transport, not a handler; counting its type arguments would let an
        // unregistered handler slip past MK1002.
        var diagnostics = await AnalyzeAsync("""
            using System.Threading;
            using System.Threading.Tasks;
            using MessagingKit;

            public sealed record Routed(string Value);

            public sealed class RoutedHandler : IMessageHandler<Routed>
            {
                public Task HandleAsync(Routed message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }

            public static class Wiring
            {
                public static void Route<TTransport, TMessage>() { }
                public static void Go() => Route<RoutedHandler, Routed>();
            }
            """);

        Assert.HasCount(1, diagnostics.Where(d => d.Id == "MK1002"));
    }

    [TestMethod]
    public async Task Reports_an_unpinned_message_name_only_when_the_rule_is_enabled()
    {
        const string Source = """
            using MessagingKit;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed record Unpinned(string Value);

            public sealed class UnpinnedHandler : IMessageHandler<Unpinned>
            {
                public Task HandleAsync(Unpinned message, MessageContext context, CancellationToken ct = default)
                    => Task.CompletedTask;
            }
            """;

        Assert.IsEmpty(
            (await AnalyzeAsync(Source)).Where(d => d.Id == "MK1004"),
            "MK1004 is off by default");

        var enabled = await AnalyzeAsync(Source, ("MK1004", ReportDiagnostic.Warn));

        Assert.HasCount(1, enabled.Where(d => d.Id == "MK1004"));
    }

    [TestMethod]
    public async Task Stays_silent_on_code_that_does_not_use_messaging()
    {
        var diagnostics = await AnalyzeAsync("public sealed record Unrelated(string Value);");

        Assert.IsEmpty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        params (string Id, ReportDiagnostic Severity)[] overrides)
    {
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        // Referenced but possibly not yet loaded into the test AppDomain.
        references.Add(MetadataReference.CreateFromFile(typeof(IMessageHandler<>).Assembly.Location));

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        if (overrides.Length > 0)
        {
            options = options.WithSpecificDiagnosticOptions(
                overrides.ToDictionary(o => o.Id, o => o.Severity));
        }

        var compilation = CSharpCompilation.Create(
            "AnalyzerProbe",
            [CSharpSyntaxTree.ParseText(source)],
            references.DistinctBy(r => r.Display).ToList(),
            options);

        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MessagingWiringAnalyzer()))
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }
}
