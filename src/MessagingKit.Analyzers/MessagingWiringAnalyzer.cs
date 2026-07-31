using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MessagingKit.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessagingWiringAnalyzer : DiagnosticAnalyzer
{
    private const string MessageAttribute = "MessagingKit.MessageAttribute";
    private const string HandlerInterface = "MessagingKit.IMessageHandler`1";

    /// <summary>
    /// Methods that register a handler, as <c>&lt;TMessage, THandler&gt;</c>. Matched by name because
    /// the same declaration is reachable from several builders. Routing methods such as
    /// <c>UseTransportFor</c> are deliberately absent — they name a transport, not a handler, and
    /// counting their type arguments would let an unregistered handler slip past MK1002.
    /// </summary>
    private static readonly ImmutableHashSet<string> HandlerRegistrationMethods = ImmutableHashSet.Create(
        "AddMessageHandler",
        "AddHandler",
        "Handles");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            Diagnostics.DuplicateMessageName,
            Diagnostics.HandlerNeverRegistered,
            Diagnostics.EmptyMessageName,
            Diagnostics.MessageNameNotPinned);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var attributeSymbol = context.Compilation.GetTypeByMetadataName(MessageAttribute);
        var handlerSymbol = context.Compilation.GetTypeByMetadataName(HandlerInterface);

        if (attributeSymbol is null && handlerSymbol is null)
        {
            // Nothing in this compilation references MessagingKit.
            return;
        }

        var state = new WiringState();

        context.RegisterSymbolAction(symbolContext => OnNamedType(symbolContext, state, attributeSymbol, handlerSymbol), SymbolKind.NamedType);
        context.RegisterOperationAction(operationContext => OnInvocation(operationContext, state), OperationKind.Invocation);
        context.RegisterCompilationEndAction(endContext => OnCompilationEnd(endContext, state));
    }

    private static void OnNamedType(
        SymbolAnalysisContext context,
        WiringState state,
        INamedTypeSymbol? attributeSymbol,
        INamedTypeSymbol? handlerSymbol)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return;
        }

        var location = type.Locations.FirstOrDefault(l => l.IsInSource);

        if (location is null)
        {
            return;
        }

        if (attributeSymbol is not null)
        {
            var attribute = type.GetAttributes().FirstOrDefault(
                a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeSymbol));

            if (attribute is not null)
            {
                var name = attribute.ConstructorArguments.FirstOrDefault().Value as string;

                if (string.IsNullOrWhiteSpace(name))
                {
                    var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? location;
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.EmptyMessageName, attributeLocation));
                }
                else
                {
                    state.MessageTypes[type] = new MessageInfo(name!, location, Pinned: true);
                }

                return;
            }
        }

        if (handlerSymbol is null)
        {
            return;
        }

        foreach (var handled in type.AllInterfaces.Where(
            i => i.IsGenericType && SymbolEqualityComparer.Default.Equals(i.ConstructedFrom, handlerSymbol)))
        {
            if (handled.TypeArguments.FirstOrDefault() is not INamedTypeSymbol messageType)
            {
                continue;
            }

            state.Handlers[type] = new HandlerInfo(messageType, location);

            if (!state.MessageTypes.ContainsKey(messageType))
            {
                var messageLocation = messageType.Locations.FirstOrDefault(l => l.IsInSource);

                if (messageLocation is not null)
                {
                    state.MessageTypes[messageType] = new MessageInfo(
                        ToKebabCase(messageType.Name),
                        messageLocation,
                        Pinned: false);
                }
            }
        }
    }

    private static void OnInvocation(OperationAnalysisContext context, WiringState state)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (!method.IsGenericMethod
            || method.TypeArguments.Length != 2
            || !HandlerRegistrationMethods.Contains(method.Name))
        {
            return;
        }

        // <TMessage, THandler> — the handler is the one being registered.
        if (method.TypeArguments[1] is INamedTypeSymbol handler)
        {
            state.RegisteredHandlers[handler] = true;
        }
    }

    private static void OnCompilationEnd(CompilationAnalysisContext context, WiringState state)
    {
        ReportDuplicateNames(context, state);

        foreach (var pair in state.Handlers)
        {
            if (state.RegisteredHandlers.ContainsKey(pair.Key))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.HandlerNeverRegistered,
                pair.Value.Location,
                pair.Key.Name,
                pair.Value.MessageType.Name));
        }

        foreach (var pair in state.MessageTypes.Where(p => !p.Value.Pinned))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.MessageNameNotPinned,
                pair.Value.Location,
                pair.Key.Name));
        }
    }

    private static void ReportDuplicateNames(CompilationAnalysisContext context, WiringState state)
    {
        var byName = new Dictionary<string, List<KeyValuePair<INamedTypeSymbol, MessageInfo>>>();

        foreach (var pair in state.MessageTypes)
        {
            if (!byName.TryGetValue(pair.Value.Name, out var list))
            {
                list = [];
                byName[pair.Value.Name] = list;
            }

            list.Add(pair);
        }

        foreach (var group in byName.Where(g => g.Value.Count > 1))
        {
            // Ordered so the reported pair does not shift between builds.
            var ordered = group.Value
                .OrderBy(p => p.Key.ToDisplayString(), System.StringComparer.Ordinal)
                .ToList();

            foreach (var entry in ordered)
            {
                var other = ordered.First(p => !SymbolEqualityComparer.Default.Equals(p.Key, entry.Key));

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.DuplicateMessageName,
                    entry.Value.Location,
                    group.Key,
                    entry.Key.Name,
                    other.Key.Name));
            }
        }
    }

    /// <summary>
    /// Mirrors MessageName.ToKebabCase. Kept in step by the analyzer's own tests — the analyzer
    /// targets netstandard2.0 and cannot reference the runtime package.
    /// </summary>
    private static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (!char.IsUpper(c))
            {
                builder.Append(c);
                continue;
            }

            var startsWord = i > 0 && !char.IsUpper(name[i - 1]);
            var endsAcronym = i > 0 && char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1]);

            if (startsWord || endsAcronym)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private sealed record MessageInfo(string Name, Location Location, bool Pinned);

    private sealed record HandlerInfo(INamedTypeSymbol MessageType, Location Location);

    private sealed class WiringState
    {
        public ConcurrentDictionary<INamedTypeSymbol, MessageInfo> MessageTypes { get; } =
            new(SymbolEqualityComparer.Default);

        public ConcurrentDictionary<INamedTypeSymbol, HandlerInfo> Handlers { get; } =
            new(SymbolEqualityComparer.Default);

        public ConcurrentDictionary<INamedTypeSymbol, bool> RegisteredHandlers { get; } =
            new(SymbolEqualityComparer.Default);
    }
}
