using System.Collections.Immutable;
using System.Runtime.Loader;
using MediatingKit.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MediatingKit.UnitTests;

/// <summary>
/// Drives the generator over in-memory projects: what it reports, what it writes, and that what it writes compiles
/// and runs, including across two projects.
/// </summary>
[TestClass]
public sealed class GeneratorTests
{
    private const string DeleteFile = """
        using System.Threading;
        using System.Threading.Tasks;
        using MediatingKit;

        public sealed record DeleteFile(string Id) : IRequest<string>;

        public sealed class DeleteFileHandler : IRequestHandler<DeleteFile, string>
        {
            public ValueTask<string> Handle(DeleteFile request, CancellationToken ct) => new("deleted " + request.Id);
        }
        """;

    private const string Composition = """
        using Microsoft.Extensions.DependencyInjection;
        using MediatingKit;

        public static class Composition
        {
            public static IServiceCollection Compose(IServiceCollection services) => services.AddMediator();
        }
        """;

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void A_project_with_a_handler_and_an_AddMediator_call_gets_a_mediator_that_compiles_cleanly()
    {
        var run = Generate(DeleteFile, Composition);

        Assert.IsEmpty(run.Diagnostics);
        Assert.IsEmpty(run.Output.GetDiagnostics(TestContext.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
        CollectionAssert.AreEquivalent(new[] { "MediatingKit.Exports.g.cs", "MediatingKit.Mediator.g.cs" }, run.Sources.Keys.ToArray());

        var mediator = run.Sources["MediatingKit.Mediator.g.cs"];
        StringAssert.Contains(mediator, "[global::System.Runtime.CompilerServices.InterceptsLocation(1, ");
        StringAssert.Contains(mediator, "file sealed class GeneratedMediator : global::MediatingKit.MediatorBase");
        StringAssert.Contains(mediator, "if (request is global::DeleteFile r0)\n");
        StringAssert.Contains(mediator, "private global::MediatingKit.RequestPipeline<global::DeleteFileHandler, global::DeleteFile, string>? _request0;");
        StringAssert.Contains(mediator, "return ((global::MediatingKit.IRequestHandler<global::DeleteFile, string>)pipeline.Handler).Handle(request, ct);");
        StringAssert.Contains(mediator, "TryAdd(services, new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::DeleteFileHandler), typeof(global::DeleteFileHandler), lifetime));");
        Assert.DoesNotContain("typeof(TResponse)", mediator, "a sealed request pins its response, so no runtime check is needed");
    }

    [TestMethod]
    public void A_request_type_that_could_be_subclassed_also_checks_the_response_type()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public record Shape(string Name) : IRequest<string>;

            public sealed class ShapeHandler : IRequestHandler<Shape, string>
            {
                public ValueTask<string> Handle(Shape request, CancellationToken ct) => new(request.Name);
            }
            """, Composition);

        Assert.IsEmpty(run.Diagnostics);
        StringAssert.Contains(run.Sources["MediatingKit.Mediator.g.cs"], "if (request is global::Shape r0 && typeof(TResponse) == typeof(string))");
    }

    [TestMethod]
    public void Without_an_AddMediator_call_only_the_exports_are_written()
    {
        var run = Generate(DeleteFile);

        Assert.IsEmpty(run.Diagnostics);
        CollectionAssert.AreEqual(new[] { "MediatingKit.Exports.g.cs" }, run.Sources.Keys.ToArray());
        StringAssert.Contains(run.Sources["MediatingKit.Exports.g.cs"], "[assembly: global::MediatingKit.MediatorExportsAttribute(typeof(global::MediatingKit.Generated.MediatorExports_Probe))]");
    }

    [TestMethod]
    public void Code_that_does_not_use_MediatingKit_gets_nothing()
    {
        var run = Generate("public sealed record Unrelated(string Value) : System.IComparable { public int CompareTo(object? o) => 0; }", withMediatingKit: false);

        Assert.IsEmpty(run.Diagnostics);
        Assert.IsEmpty(run.Sources);
    }

    [TestMethod]
    public void A_request_without_a_handler_is_MED1001()
    {
        var run = Generate(DeleteFile, """
            using MediatingKit;

            public sealed record ArchiveFile(string Id) : IRequest<bool>;
            public sealed record Ping : IRequest;
            public sealed record ListFiles : IStreamRequest<string>;
            """);

        var missing = run.Diagnostics.Where(d => d.Id == "MED1001").Select(d => d.GetMessage()).OrderBy(m => m, StringComparer.Ordinal).ToList();

        Assert.HasCount(3, missing);
        Assert.AreEqual("'ArchiveFile' has no handler; add a class implementing IRequestHandler<ArchiveFile, bool>", missing[0]);
        Assert.AreEqual("'ListFiles' has no handler; add a class implementing IStreamRequestHandler<ListFiles, string>", missing[1]);
        Assert.AreEqual("'Ping' has no handler; add a class implementing IRequestHandler<Ping>", missing[2]);
        Assert.IsTrue(run.Diagnostics.All(d => d.Severity == DiagnosticSeverity.Error));
        Assert.AreEqual("ArchiveFile", SourceAt(run, run.Diagnostics.First(d => d.GetMessage().Contains("ArchiveFile", StringComparison.Ordinal))));
    }

    [TestMethod]
    public void A_contracts_project_with_no_handlers_of_its_own_is_not_flagged()
    {
        var run = Generate("""
            using MediatingKit;

            public sealed record ArchiveFile(string Id) : IRequest<bool>;
            """);

        Assert.IsEmpty(run.Diagnostics);
    }

    [TestMethod]
    public void A_request_handled_through_its_base_type_is_not_missing_a_handler()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public abstract record Command : IRequest<int>;
            public sealed record Increment : Command;

            public sealed class CommandHandler : IRequestHandler<Command, int>
            {
                public ValueTask<int> Handle(Command request, CancellationToken ct) => new(1);
            }
            """, Composition);

        Assert.IsEmpty(run.Diagnostics);
    }

    [TestMethod]
    public void Two_handlers_for_one_request_are_MED1002_on_both()
    {
        var run = Generate(DeleteFile, """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public sealed class OtherDeleteFileHandler : IRequestHandler<DeleteFile, string>
            {
                public ValueTask<string> Handle(DeleteFile request, CancellationToken ct) => new("again");
            }
            """);

        var duplicates = run.Diagnostics.Where(d => d.Id == "MED1002").ToList();

        Assert.HasCount(2, duplicates);
        Assert.AreEqual("'DeleteFile' is handled by more than one handler: DeleteFileHandler, OtherDeleteFileHandler; a request has exactly one", duplicates[0].GetMessage());
        CollectionAssert.AreEquivalent(new[] { "DeleteFileHandler", "OtherDeleteFileHandler" }, duplicates.Select(d => SourceAt(run, d)).ToArray());
    }

    [TestMethod]
    public void Several_notification_handlers_are_fine()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public sealed record FileDeleted(string Id) : INotification;

            public sealed class First : INotificationHandler<FileDeleted>
            {
                public ValueTask Handle(FileDeleted notification, CancellationToken ct) => default;
            }

            public sealed class Second : INotificationHandler<FileDeleted>
            {
                public ValueTask Handle(FileDeleted notification, CancellationToken ct) => default;
            }
            """, Composition);

        Assert.IsEmpty(run.Diagnostics);
        StringAssert.Contains(run.Sources["MediatingKit.Mediator.g.cs"], "var set = _notification0 ??= new PublishSet0(GetHandler<global::First>(), GetHandler<global::Second>());");
    }

    [TestMethod]
    public void A_type_with_two_responses_is_MED1003()
    {
        var run = Generate("""
            using MediatingKit;

            public sealed record Confused : IRequest<int>, IRequest<string>;
            public sealed record AlsoConfused : IRequest, IStreamRequest<int>;
            """);

        var ambiguous = run.Diagnostics.Where(d => d.Id == "MED1003").Select(d => d.GetMessage()).OrderBy(m => m, StringComparer.Ordinal).ToList();

        Assert.HasCount(2, ambiguous);
        StringAssert.Contains(ambiguous[0], "'AlsoConfused' implements");
        StringAssert.Contains(ambiguous[1], "'Confused' implements MediatingKit.IRequest<int> and MediatingKit.IRequest<string>");
    }

    [TestMethod]
    public void A_generic_or_hidden_handler_is_MED1004()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public sealed record Get<T>(T Id) : IRequest<T>;

            public sealed class GetHandler<T> : IRequestHandler<Get<T>, T>
            {
                public ValueTask<T> Handle(Get<T> request, CancellationToken ct) => new(request.Id);
            }

            public sealed record Hidden : IRequest<int>;

            public static class Outer
            {
                private sealed class HiddenHandler : IRequestHandler<Hidden, int>
                {
                    public ValueTask<int> Handle(Hidden request, CancellationToken ct) => new(1);
                }
            }
            """);

        var unusable = run.Diagnostics.Where(d => d.Id == "MED1004").Select(d => d.GetMessage()).OrderBy(m => m, StringComparer.Ordinal).ToList();

        Assert.HasCount(2, unusable);
        StringAssert.StartsWith(unusable[0], "'GetHandler' cannot be a handler: it is generic");
        StringAssert.StartsWith(unusable[1], "'HiddenHandler' cannot be a handler: it is not visible");

        // The hidden handler is not dispatched to, so its request has none.
        Assert.ContainsSingle(run.Diagnostics.Where(d => d.Id == "MED1001"));
    }

    [TestMethod]
    public void Abstract_handlers_are_skipped_and_their_concrete_subclasses_used()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public sealed record Ping : IRequest<int>;

            public abstract class HandlerBase<TRequest> : IRequestHandler<TRequest, int> where TRequest : IRequest<int>
            {
                public ValueTask<int> Handle(TRequest request, CancellationToken ct) => new(1);
            }

            public sealed class PingHandler : HandlerBase<Ping>;
            """, Composition);

        Assert.IsEmpty(run.Diagnostics);
        StringAssert.Contains(run.Sources["MediatingKit.Mediator.g.cs"], "GetHandler<global::PingHandler>()");
    }

    [TestMethod]
    public async Task An_internal_handler_in_a_referenced_project_is_dispatched_to_through_its_exports()
    {
        var module = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            namespace Files;

            public sealed record DeleteFile(string Id) : IRequest<string>;
            public sealed record FileDeleted(string Id) : INotification;

            internal sealed class DeleteFileHandler : IRequestHandler<DeleteFile, string>
            {
                public ValueTask<string> Handle(DeleteFile request, CancellationToken ct) => new("module deleted " + request.Id);
            }

            internal sealed class Audit : INotificationHandler<FileDeleted>
            {
                public static int Count;
                public ValueTask Handle(FileDeleted notification, CancellationToken ct) { Count++; return default; }
            }
            """, assemblyName: "Files");

        Assert.IsEmpty(module.Diagnostics);
        var moduleImage = Emit(module.Output);

        var host = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;
            using Microsoft.Extensions.DependencyInjection;

            public sealed record Ping : IRequest<string>;

            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => new("pong");
            }

            public static class Program
            {
                public static async Task<string> Run()
                {
                    await using var provider = new ServiceCollection().AddMediator().BuildServiceProvider();
                    await using var scope = provider.CreateAsyncScope();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                    await mediator.Publish(new Files.FileDeleted("a"));
                    return await mediator.Send(new Files.DeleteFile("a")) + " / " + await mediator.Send(new Ping());
                }
            }
            """, assemblyName: "Host", references: [MetadataReference.CreateFromImage(moduleImage)]);

        Assert.IsEmpty(host.Diagnostics);
        Assert.IsEmpty(host.Output.GetDiagnostics(TestContext.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));
        var mediator = host.Sources["MediatingKit.Mediator.g.cs"];
        StringAssert.Contains(mediator, "global::MediatingKit.Generated.MediatorExports_Files.Register(services, lifetime);");
        StringAssert.Contains(mediator, "global::MediatingKit.Generated.MediatorExports_Files.Handler0(Services)");

        var context = new AssemblyLoadContext("generator-probe", isCollectible: true);

        try
        {
            context.LoadFromStream(new MemoryStream(moduleImage));
            var assembly = context.LoadFromStream(new MemoryStream(Emit(host.Output)));
            var run = (Task<string>)assembly.GetType("Program")!.GetMethod("Run")!.Invoke(null, null)!;

            Assert.AreEqual("module deleted a / pong", await run);
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public void A_handler_in_this_project_and_one_in_a_referenced_project_for_the_same_request_are_MED1002()
    {
        var module = Generate(DeleteFile, assemblyName: "Files");
        var host = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            public sealed class HostDeleteFileHandler : IRequestHandler<DeleteFile, string>
            {
                public ValueTask<string> Handle(DeleteFile request, CancellationToken ct) => new("host");
            }
            """, assemblyName: "Host", references: [MetadataReference.CreateFromImage(Emit(module.Output))]);

        var duplicate = host.Diagnostics.Single(d => d.Id == "MED1002");

        StringAssert.Contains(duplicate.GetMessage(), "HostDeleteFileHandler");
        StringAssert.Contains(duplicate.GetMessage(), "Files: IRequestHandler<DeleteFile, string>");
    }

    [TestMethod]
    public void Requests_with_internal_messages_are_not_exported()
    {
        var run = Generate("""
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            internal sealed record Secret : IRequest<int>;

            internal sealed class SecretHandler : IRequestHandler<Secret, int>
            {
                public ValueTask<int> Handle(Secret request, CancellationToken ct) => new(1);
            }
            """);

        Assert.IsEmpty(run.Diagnostics);
        Assert.IsEmpty(run.Sources);
    }

    /// <summary>
    /// The whole generated mediator for a project with every kind of message, compared with the checked-in snapshot.
    /// After an intended change, run the tests with MEDIATINGKIT_UPDATE_SNAPSHOTS=1 and review the diff.
    /// </summary>
    [TestMethod]
    public void The_generated_mediator_matches_its_snapshot()
    {
        var run = Generate("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using MediatingKit;

            namespace Files;

            public sealed record DeleteFile(string Id) : IRequest<bool>;
            public sealed record Touch(string Id) : IRequest;
            public sealed record ListFiles(string Prefix) : IStreamRequest<string>;
            public sealed record FileDeleted(string Id) : INotification;

            public sealed class DeleteFileHandler : IRequestHandler<DeleteFile, bool>
            {
                public ValueTask<bool> Handle(DeleteFile request, CancellationToken ct) => new(true);
            }

            internal sealed class TouchHandler : IRequestHandler<Touch>
            {
                public ValueTask Handle(Touch request, CancellationToken ct) => default;
            }

            public sealed class ListFilesHandler : IStreamRequestHandler<ListFiles, string>
            {
                public async IAsyncEnumerable<string> Handle(ListFiles request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
                {
                    await Task.Yield();
                    yield return request.Prefix;
                }
            }

            public sealed class Audit : INotificationHandler<FileDeleted>
            {
                public ValueTask Handle(FileDeleted notification, CancellationToken ct) => default;
            }

            public sealed class Everything : INotificationHandler<INotification>
            {
                public ValueTask Handle(INotification notification, CancellationToken ct) => default;
            }
            """, Composition);

        Assert.IsEmpty(run.Diagnostics);
        Assert.IsEmpty(run.Output.GetDiagnostics(TestContext.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning));

        foreach (var (hintName, source) in run.Sources)
        {
            MatchSnapshot(hintName, source);
        }
    }

    [TestMethod]
    public void An_edit_that_changes_no_message_or_handler_reuses_the_generated_output()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "MediatingKit.Generated")]);
        var compilation = CSharpCompilation.Create(
            "Probe",
            [CSharpSyntaxTree.ParseText(DeleteFile, parseOptions, path: "Source0.cs"), CSharpSyntaxTree.ParseText(Composition, parseOptions, path: "Source1.cs")],
            PlatformReferences(withMediatingKit: true),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MediatorGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.CancellationToken);

        var edited = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "public static class Unrelated { public static int Answer() => 42; }", parseOptions, path: "Source2.cs"));
        driver = driver.RunGenerators(edited, TestContext.CancellationToken);

        var outputs = driver.GetRunResult().Results.Single().TrackedOutputSteps.SelectMany(s => s.Value).SelectMany(s => s.Outputs).ToList();

        Assert.IsNotEmpty(outputs);
        Assert.IsTrue(
            outputs.All(o => o.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            string.Join(", ", outputs.Select(o => o.Reason)));
    }

    private static void MatchSnapshot(string hintName, string actual, [System.Runtime.CompilerServices.CallerFilePath] string testFile = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots", hintName.Replace(".g.cs", ".verified.txt", StringComparison.Ordinal));

        if (Environment.GetEnvironmentVariable("MEDIATINGKIT_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.IsTrue(File.Exists(path), $"No snapshot at {path}; run with MEDIATINGKIT_UPDATE_SNAPSHOTS=1 to write it.");
        Assert.AreEqual(File.ReadAllText(path).ReplaceLineEndings("\n"), actual, $"{hintName} differs from {path}.");
    }

    /// <summary>The source a diagnostic points at. Generator diagnostics carry a path and span rather than a tree.</summary>
    private static string SourceAt(GeneratorRun run, Diagnostic diagnostic) =>
        run.Output.SyntaxTrees
            .Single(t => t.FilePath == diagnostic.Location.GetLineSpan().Path)
            .GetText()
            .ToString(diagnostic.Location.SourceSpan);

    private static byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

    private static GeneratorRun Generate(
        string source,
        string? second = null,
        string assemblyName = "Probe",
        IEnumerable<MetadataReference>? references = null,
        bool withMediatingKit = true)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "MediatingKit.Generated")]);

        var trees = new[] { source, second }
            .Where(s => s is not null)
            // LF whatever the checkout uses, so the interceptor's location data (a hash of the text) is the same everywhere.
            .Select((s, i) => CSharpSyntaxTree.ParseText(s!.ReplaceLineEndings("\n"), parseOptions, path: $"Source{i}.cs"))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            PlatformReferences(withMediatingKit).Concat(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create([new MediatorGenerator().AsSourceGenerator()], parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var result = driver.GetRunResult();

        return new GeneratorRun(
            diagnostics,
            output,
            result.Results.SelectMany(r => r.GeneratedSources).ToDictionary(s => s.HintName, s => s.SourceText.ToString()));
    }

    private static IEnumerable<MetadataReference> PlatformReferences(bool withMediatingKit) =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => withMediatingKit || !Path.GetFileName(path).StartsWith("MediatingKit", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).StartsWith("MediatingKit.UnitTests", StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path));

    private sealed record GeneratorRun(ImmutableArray<Diagnostic> Diagnostics, Compilation Output, Dictionary<string, string> Sources);
}
