using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MediatingKit.Generators;

/// <summary>Reports the diagnostics and writes the exports class and the mediator.</summary>
internal static class Emitter
{
    private const string Unit = "global::MediatingKit.Unit";
    private const string VoidRequest = "global::MediatingKit.IRequest";

    public static void Execute(
        SourceProductionContext context,
        ImmutableArray<DeclaredType> declared,
        ImmutableArray<InterceptedCall> calls,
        ReferencedHandlers references)
    {
        if (!references.IsMediatingKitReferenced)
        {
            return;
        }

        // A partial type is seen once per declaration; its model is the same each time.
        var types = declared
            .GroupBy(t => t.FullType, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        foreach (var diagnostic in types.SelectMany(t => t.Diagnostics))
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        var own = types.SelectMany(t => t.Handlers).ToList();
        var all = own.Concat(references.Handlers).ToList();

        ReportDuplicates(context, all);

        if (types.Any(t => t.IsHandler))
        {
            ReportMissing(context, types, all);
        }

        var exportable = own.Where(h => h.IsExportable).ToList();

        if (exportable.Count > 0)
        {
            context.AddSource("MediatingKit.Exports.g.cs", EmitExports(references.AssemblyName, exportable));
        }

        if (calls.Length > 0)
        {
            context.AddSource("MediatingKit.Mediator.g.cs", EmitMediator(calls, own, all, references.ExportClasses));
        }
    }

    private static void ReportDuplicates(SourceProductionContext context, List<HandlerInfo> all)
    {
        var groups = all
            .Where(h => h.Kind != HandlerKind.Notification)
            .GroupBy(h => (Stream: h.Kind == HandlerKind.Stream, h.MessagePattern))
            .Where(g => g.Select(h => h.HandlerType).Distinct(StringComparer.Ordinal).Count() > 1);

        foreach (var group in groups)
        {
            var names = string.Join(", ", group.Select(h => h.HandlerName).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal));
            var located = group.Where(h => h.Location is not null).ToList();
            var message = group.First().MessageName;

            if (located.Count == 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateHandler, Location.None, message, names));
                continue;
            }

            foreach (var handler in located)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateHandler, handler.Location!.ToLocation(), message, names));
            }
        }
    }

    private static void ReportMissing(SourceProductionContext context, List<DeclaredType> types, List<HandlerInfo> all)
    {
        foreach (var type in types.Where(t => t.RequestHint is not null))
        {
            var accepted = new HashSet<string>(type.SuperTypes, StringComparer.Ordinal) { type.FullType };
            var handled = all.Any(h =>
                (type.IsStreamRequest ? h.Kind == HandlerKind.Stream : h.IsRequestKind) && accepted.Contains(h.MessagePattern));

            if (!handled)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.MissingHandler,
                    type.Location?.ToLocation() ?? Location.None,
                    type.Name,
                    type.RequestHint));
            }
        }
    }

    private static string EmitExports(string assemblyName, List<HandlerInfo> handlers)
    {
        var className = "MediatorExports_" + Sanitize(assemblyName);
        var w = new CodeWriter();

        WriteHeader(w);
        w.Line($"[assembly: global::MediatingKit.MediatorExportsAttribute(typeof(global::MediatingKit.Generated.{className}))]");
        w.Line();
        w.Line("namespace MediatingKit.Generated");
        w.Open();
        w.Line("/// <summary>This project's handlers, for the mediator generated in a project that references it. Not for direct use.</summary>");
        w.Line("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        WriteGeneratedCode(w);
        w.Line($"public static class {className}");
        w.Open();
        w.Line("/// <summary>Registers the handlers listed here.</summary>");
        w.Line("public static void Register(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)");
        w.Open();

        foreach (var handlerType in handlers.Select(h => h.HandlerType).Distinct(StringComparer.Ordinal))
        {
            WriteTryAdd(w, handlerType);
        }

        w.Close();

        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i];
            w.Line();
            w.Line($"/// <summary>Resolves {Escape(handler.HandlerName)}.</summary>");
            w.Line($"public static {handler.InterfaceType} Handler{i}(global::System.IServiceProvider services) =>");
            w.Line($"    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<{handler.HandlerType}>(services);");
        }

        w.Close();
        w.Close();

        return w.ToString();
    }

    private static string EmitMediator(
        ImmutableArray<InterceptedCall> calls,
        List<HandlerInfo> own,
        List<HandlerInfo> all,
        EquatableArray<string> exportClasses)
    {
        var requests = Cases(all.Where(h => h.IsRequestKind));
        var streams = Cases(all.Where(h => h.Kind == HandlerKind.Stream));
        var notifications = NotificationCases(all.Where(h => h.Kind == HandlerKind.Notification).ToList());

        var w = new CodeWriter();

        WriteHeader(w);
        w.Line("namespace MediatingKit.Generated");
        w.Open();

        // The interceptor.
        WriteGeneratedCode(w);
        w.Line("file static class MediatorRegistrationInterceptor");
        w.Open();

        foreach (var call in calls.Distinct())
        {
            w.Line($"// {call.DisplayLocation}");
            w.Line($"[global::System.Runtime.CompilerServices.InterceptsLocation({call.Version}, {Literal(call.Data)})]");
        }

        w.Line("public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddMediator(");
        w.Line("    this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
        w.Line("    global::System.Action<global::MediatingKit.MediatorOptions>? configure = null) =>");
        w.Line("    global::MediatingKit.MediatorRegistration.Register(");
        w.Line("        services,");
        w.Line("        configure,");
        w.Line("        typeof(GeneratedMediator),");
        w.Line("        static services => new GeneratedMediator(services),");
        w.Line("        RegisterHandlers);");
        w.Line();
        w.Line("private static void RegisterHandlers(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime)");
        w.Open();

        foreach (var handlerType in own.Select(h => h.HandlerType).Distinct(StringComparer.Ordinal))
        {
            WriteTryAdd(w, handlerType);
        }

        foreach (var exports in exportClasses)
        {
            w.Line($"{exports}.Register(services, lifetime);");
        }

        w.Close();
        w.Close();
        w.Line();

        // The mediator.
        w.Line("/// <summary>The mediator for this project: one typed field and one type check per handler.</summary>");
        WriteGeneratedCode(w);
        w.Line("file sealed class GeneratedMediator : global::MediatingKit.MediatorBase");
        w.Open();
        w.Line("public GeneratedMediator(global::System.IServiceProvider services)");
        w.Line("    : base(services)");
        w.Open();
        w.Close();

        WriteSend(w, requests);
        WriteSendVoid(w, requests);
        WriteRequestCases(w, requests);
        WriteStreams(w, streams);
        WritePublish(w, notifications);

        w.Close();
        w.Close();
        w.Line();

        w.Line("namespace System.Runtime.CompilerServices");
        w.Open();
        WriteGeneratedCode(w);
        w.Line("[global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        w.Line("file sealed class InterceptsLocationAttribute : global::System.Attribute");
        w.Open();
        w.Line("public InterceptsLocationAttribute(int version, string data)");
        w.Open();
        w.Line("_ = version;");
        w.Line("_ = data;");
        w.Close();
        w.Close();
        w.Close();

        return w.ToString();
    }

    private static void WriteSend(CodeWriter w, List<HandlerInfo> requests)
    {
        w.Line();
        w.Line("public override global::System.Threading.Tasks.ValueTask<TResponse> Send<TResponse>(global::MediatingKit.IRequest<TResponse> request, global::System.Threading.CancellationToken ct = default)");
        w.Open();

        for (var i = 0; i < requests.Count; i++)
        {
            var handler = requests[i];
            var response = handler.Kind == HandlerKind.VoidRequest ? Unit : handler.ResponseType;
            var call = handler.Kind == HandlerKind.VoidRequest ? $"ToUnit(Send{i}(r{i}, ct))" : $"Send{i}(r{i}, ct)";

            w.Line($"if (request is {handler.MessagePattern} r{i}{ResponseGuard(handler)})");
            w.Open();
            w.Line($"var response = {call};");
            w.Line($"return global::System.Runtime.CompilerServices.Unsafe.As<global::System.Threading.Tasks.ValueTask<{response}>, global::System.Threading.Tasks.ValueTask<TResponse>>(ref response);");
            w.Close();
            w.Line();
        }

        w.Line("return ThrowNoHandler<global::System.Threading.Tasks.ValueTask<TResponse>>(request);");
        w.Close();
    }

    private static void WriteSendVoid(CodeWriter w, List<HandlerInfo> requests)
    {
        w.Line();
        w.Line("public override global::System.Threading.Tasks.ValueTask Send(global::MediatingKit.IRequest request, global::System.Threading.CancellationToken ct = default)");
        w.Open();

        for (var i = 0; i < requests.Count; i++)
        {
            var handler = requests[i];

            // A pattern the compiler can prove never matches an IRequest is an error, so only requests that are one.
            if (!handler.MessageIsVoidRequest && handler.MessagePattern != VoidRequest)
            {
                continue;
            }

            if (handler.Kind == HandlerKind.VoidRequest)
            {
                w.Line($"if (request is {handler.MessagePattern} r{i}) return Send{i}(r{i}, ct);");
            }
            else if (handler.ResponsePattern == Unit)
            {
                w.Line($"if (request is {handler.MessagePattern} r{i}) return ToValueTask(Send{i}(r{i}, ct));");
            }
        }

        w.Line("return ThrowNoHandler<global::System.Threading.Tasks.ValueTask>(request);");
        w.Close();
    }

    private static void WriteRequestCases(CodeWriter w, List<HandlerInfo> requests)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            var handler = requests[i];
            var holder = handler.ExportAccessor is null ? handler.HandlerType : handler.InterfaceType;
            var resolve = Resolve(handler);
            var span = Literal("send " + handler.MessageName);
            var type = Literal(handler.MessageFullName);

            w.Line();

            if (handler.Kind == HandlerKind.VoidRequest)
            {
                w.Line($"private global::MediatingKit.VoidRequestPipeline<{holder}, {handler.MessageType}>? _request{i};");
                w.Line();
                w.Line($"private global::System.Threading.Tasks.ValueTask Send{i}({handler.MessageType} request, global::System.Threading.CancellationToken ct)");
                w.Open();
                w.Line($"var pipeline = _request{i} ??= CreateVoidPipeline<{holder}, {handler.MessageType}>({resolve});");
                w.Line($"if (pipeline.HasBehaviors || IsInstrumented) return ToValueTask(Dispatch(pipeline.Invoke, request, {span}, {type}, ct));");
                w.Line($"return (({handler.InterfaceType})pipeline.Handler).Handle(request, ct);");
                w.Close();
            }
            else
            {
                w.Line($"private global::MediatingKit.RequestPipeline<{holder}, {handler.MessageType}, {handler.ResponseType}>? _request{i};");
                w.Line();
                w.Line($"private global::System.Threading.Tasks.ValueTask<{handler.ResponseType}> Send{i}({handler.MessageType} request, global::System.Threading.CancellationToken ct)");
                w.Open();
                w.Line($"var pipeline = _request{i} ??= CreatePipeline<{holder}, {handler.MessageType}, {handler.ResponseType}>({resolve});");
                w.Line($"if (pipeline.HasBehaviors || IsInstrumented) return Dispatch(pipeline.Invoke, request, {span}, {type}, ct);");
                w.Line($"return (({handler.InterfaceType})pipeline.Handler).Handle(request, ct);");
                w.Close();
            }
        }
    }

    private static void WriteStreams(CodeWriter w, List<HandlerInfo> streams)
    {
        for (var i = 0; i < streams.Count; i++)
        {
            w.Line();
            w.Line($"private {streams[i].InterfaceType}? _stream{i};");
        }

        w.Line();
        w.Line("public override global::System.Collections.Generic.IAsyncEnumerable<TResponse> CreateStream<TResponse>(global::MediatingKit.IStreamRequest<TResponse> request, global::System.Threading.CancellationToken ct = default)");
        w.Open();

        for (var i = 0; i < streams.Count; i++)
        {
            var handler = streams[i];

            w.Line($"if (request is {handler.MessagePattern} s{i}{ResponseGuard(handler)})");
            w.Open();
            w.Line($"var handler = _stream{i} ??= {Resolve(handler)};");
            w.Line($"return global::System.Runtime.CompilerServices.Unsafe.As<global::System.Collections.Generic.IAsyncEnumerable<TResponse>>(handler.Handle(s{i}, ct));");
            w.Close();
            w.Line();
        }

        w.Line("return ThrowNoHandler<global::System.Collections.Generic.IAsyncEnumerable<TResponse>>(request);");
        w.Close();
    }

    private static void WritePublish(CodeWriter w, List<(HandlerInfo Case, List<HandlerInfo> Handlers)> notifications)
    {
        for (var i = 0; i < notifications.Count; i++)
        {
            w.Line();
            w.Line($"private global::MediatingKit.INotificationHandler<{notifications[i].Case.MessageType}>[]? _notification{i};");
        }

        w.Line();
        w.Line("public override global::System.Threading.Tasks.ValueTask Publish<TNotification>(TNotification notification, global::System.Threading.CancellationToken ct = default)");
        w.Open();

        for (var i = 0; i < notifications.Count; i++)
        {
            var (match, handlers) = notifications[i];

            w.Line($"if (notification is {match.MessagePattern} n{i})");
            w.Open();
            w.Line($"var handlers = _notification{i} ??= [{string.Join(", ", handlers.Select(Resolve))}];");
            w.Line($"return PublishToHandlers(handlers, n{i}, {Literal("publish " + match.MessageName)}, {Literal(match.MessageFullName)}, ct);");
            w.Close();
            w.Line();
        }

        w.Line("// A notification nobody handles is not an error.");
        w.Line("return notification is null ? ThrowNoHandler<global::System.Threading.Tasks.ValueTask>(null) : default;");
        w.Close();
    }

    /// <summary>
    /// One case per message type, most derived first, so a request matched through a base-type handler never shadows
    /// one with its own.
    /// </summary>
    private static List<HandlerInfo> Cases(IEnumerable<HandlerInfo> handlers) =>
        handlers
            .GroupBy(h => h.MessagePattern, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderByDescending(h => h.Depth)
            .ThenBy(h => h.MessagePattern, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// One case per notification type with a handler, each listing the handlers for the type itself, then those for
    /// its base types and interfaces, most specific first.
    /// </summary>
    private static List<(HandlerInfo Case, List<HandlerInfo> Handlers)> NotificationCases(List<HandlerInfo> handlers) =>
        Cases(handlers)
            .Select(match =>
            {
                var accepted = new HashSet<string>(match.MessageSuperTypes, StringComparer.Ordinal);
                var applicable = handlers
                    .Where(h => h.MessagePattern == match.MessagePattern || accepted.Contains(h.MessagePattern))
                    .GroupBy(h => h.ExportAccessor ?? h.HandlerType, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(h => h.MessagePattern == match.MessagePattern ? 0 : 1)
                    .ThenByDescending(h => h.Depth)
                    .ThenBy(h => h.HandlerType, StringComparer.Ordinal)
                    .ToList();

                return (match, applicable);
            })
            .ToList();

    /// <summary>
    /// A sealed message type pins the response type, which the generic method relies on to reinterpret the task.
    /// Anything that could be subclassed is checked at runtime as well.
    /// </summary>
    private static string ResponseGuard(HandlerInfo handler)
    {
        if (handler.MessageIsExact)
        {
            return string.Empty;
        }

        var response = handler.Kind == HandlerKind.VoidRequest ? Unit : handler.ResponsePattern;
        return $" && typeof(TResponse) == typeof({response})";
    }

    private static string Resolve(HandlerInfo handler) =>
        handler.ExportAccessor is null ? $"GetHandler<{handler.HandlerType}>()" : $"{handler.ExportAccessor}(Services)";

    private static void WriteTryAdd(CodeWriter w, string handlerType) =>
        w.Line($"global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({StripNullable(handlerType)}), typeof({StripNullable(handlerType)}), lifetime));");

    private static void WriteHeader(CodeWriter w)
    {
        w.Line("// <auto-generated/>");
        w.Line("#nullable enable");
        w.Line("#pragma warning disable CS1591");
        w.Line();
    }

    private static void WriteGeneratedCode(CodeWriter w) =>
        w.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"MediatingKit.Generators\", \"1.0.0\")]");

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string StripNullable(string type) => type.TrimEnd('?');

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private sealed class CodeWriter
    {
        private readonly StringBuilder _builder = new();
        private int _indent;

        public void Line(string text = "")
        {
            if (text.Length > 0)
            {
                _builder.Append(' ', _indent * 4);
                _builder.Append(text);
            }

            _builder.Append('\n');
        }

        public void Open()
        {
            Line("{");
            _indent++;
        }

        public void Close()
        {
            _indent--;
            Line("}");
        }

        public override string ToString() => _builder.ToString();
    }
}
