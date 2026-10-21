using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MediatingKit.Generators;

/// <summary>Turns syntax and symbols into the equatable models the emitter works from.</summary>
internal static class Discovery
{
    private const string MediatingKitAssembly = "MediatingKit";
    private const string ExtensionsType = "MediatingKit.MediatingServiceCollectionExtensions";
    private const string ExportsAttribute = "MediatingKit.MediatorExportsAttribute";

    /// <summary>Fully qualified, with nullable annotations: for signatures and generic arguments.</summary>
    private static readonly SymbolDisplayFormat Qualified =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Fully qualified, without nullable annotations: for <c>is</c> patterns and <c>typeof</c>.</summary>
    private static readonly SymbolDisplayFormat Pattern = SymbolDisplayFormat.FullyQualifiedFormat;

    public static bool IsTypeCandidate(SyntaxNode node) =>
        node is ClassDeclarationSyntax { BaseList: not null }
            or RecordDeclarationSyntax { BaseList: not null }
            or StructDeclarationSyntax { BaseList: not null };

    public static bool IsCallCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AddMediator" }
                or IdentifierNameSyntax { Identifier.ValueText: "AddMediator" },
        };

    public static DeclaredType? InspectType(GeneratorSyntaxContext context, CancellationToken ct)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, ct) is not INamedTypeSymbol type)
        {
            return null;
        }

        var known = KnownTypes.Create(context.SemanticModel.Compilation);

        if (known is null)
        {
            return null;
        }

        var declaration = (TypeDeclarationSyntax)context.Node;
        var location = LocationInfo.From(declaration.Identifier.GetLocation());
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        var requestInterfaces = known.RequestInterfaces(type);
        string? requestHint = null;
        var isStream = false;

        if (requestInterfaces.Length > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                Diagnostics.AmbiguousResponse,
                location,
                new[] { type.Name, string.Join(" and ", requestInterfaces.Select(i => i.ToDisplayString())) }.ToEquatableArray()));
        }
        else if (requestInterfaces.Length == 1 && !type.IsAbstract && !type.IsGenericType)
        {
            var request = requestInterfaces[0];
            isStream = SymbolEqualityComparer.Default.Equals(request.OriginalDefinition, known.StreamRequest);
            var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var response = request.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            requestHint = isStream
                ? $"IStreamRequestHandler<{name}, {response}>"
                : known.IsVoidRequest(type) ? $"IRequestHandler<{name}>" : $"IRequestHandler<{name}, {response}>";
        }

        var handlers = ImmutableArray<HandlerInfo>.Empty;
        var handlerInterfaces = type.AllInterfaces.Where(known.IsHandlerInterface).ToImmutableArray();
        var isHandler = handlerInterfaces.Length > 0 && type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic;

        if (isHandler)
        {
            var problem = type.IsGenericType
                ? "it is generic; close it over concrete types, or write one handler per request"
                : !IsVisibleInAssembly(type)
                    ? "it is not visible to the rest of its project; make it internal or public"
                    : null;

            if (problem is not null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    Diagnostics.UnusableHandler,
                    location,
                    new[] { type.Name, problem }.ToEquatableArray()));
            }
            else
            {
                var handlerType = type.ToDisplayString(Qualified);
                handlers = handlerInterfaces
                    .Select(i => CreateHandler(known, i, handlerType, type.Name, exportAccessor: null, isExportable: true, location))
                    .Where(h => h is not null)
                    .Select(h => h!)
                    .ToImmutableArray();
            }
        }

        if (requestHint is null && !isHandler && diagnostics.Count == 0)
        {
            return null;
        }

        return new DeclaredType(
            type.ToDisplayString(Qualified),
            type.Name,
            location,
            requestHint,
            isStream,
            SuperTypes(type).ToEquatableArray(),
            isHandler,
            handlers.ToEquatableArray(),
            diagnostics.ToImmutable().ToEquatableArray());
    }

    public static InterceptedCall? InspectCall(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        method = method.ReducedFrom ?? method;

        if (method.Name != "AddMediator" || method.ContainingType?.ToDisplayString() != ExtensionsType)
        {
            return null;
        }

        var location = context.SemanticModel.GetInterceptableLocation(invocation, ct);

        return location is null ? null : new InterceptedCall(location.Version, location.Data, location.GetDisplayLocation());
    }

    public static ReferencedHandlers InspectReferences(Compilation compilation, CancellationToken ct)
    {
        var known = KnownTypes.Create(compilation);
        var exportsAttribute = compilation.GetTypeByMetadataName(ExportsAttribute);

        if (known is null || exportsAttribute is null)
        {
            return ReferencedHandlers.None;
        }

        var exportClasses = ImmutableArray.CreateBuilder<string>();
        var handlers = ImmutableArray.CreateBuilder<HandlerInfo>();

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            ct.ThrowIfCancellationRequested();

            // Only assemblies built against MediatingKit can carry exports; skipping the rest keeps this cheap.
            if (!assembly.Modules.Any(m => m.ReferencedAssemblies.Any(r => r.Name == MediatingKitAssembly)))
            {
                continue;
            }

            foreach (var attribute in assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exportsAttribute)
                    || attribute.ConstructorArguments.Length != 1
                    || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol exports
                    || !compilation.IsSymbolAccessibleWithin(exports, compilation.Assembly))
                {
                    continue;
                }

                var exportsName = exports.ToDisplayString(Qualified);
                exportClasses.Add(exportsName);

                foreach (var member in exports.GetMembers().OfType<IMethodSymbol>())
                {
                    if (member is not { IsStatic: true, Parameters.Length: 1, ReturnType: INamedTypeSymbol handlerInterface }
                        || member.Parameters[0].Type.Name != "IServiceProvider"
                        || !known.IsHandlerInterface(handlerInterface)
                        || !handlerInterface.TypeArguments.All(t => compilation.IsSymbolAccessibleWithin(t, compilation.Assembly)))
                    {
                        continue;
                    }

                    var handler = CreateHandler(
                        known,
                        handlerInterface,
                        handlerInterface.ToDisplayString(Qualified),
                        $"{assembly.Name}: {handlerInterface.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
                        $"{exportsName}.{member.Name}",
                        isExportable: false,
                        location: null);

                    if (handler is not null)
                    {
                        handlers.Add(handler);
                    }
                }
            }
        }

        return new ReferencedHandlers(
            true,
            compilation.AssemblyName ?? "Assembly",
            exportClasses.ToImmutable().ToEquatableArray(),
            handlers.ToImmutable().ToEquatableArray());
    }

    private static HandlerInfo? CreateHandler(
        KnownTypes known,
        INamedTypeSymbol handlerInterface,
        string handlerType,
        string handlerName,
        string? exportAccessor,
        bool isExportable,
        LocationInfo? location)
    {
        var definition = handlerInterface.OriginalDefinition;
        var kind =
            SymbolEqualityComparer.Default.Equals(definition, known.RequestHandler) ? HandlerKind.Request
            : SymbolEqualityComparer.Default.Equals(definition, known.VoidRequestHandler) ? HandlerKind.VoidRequest
            : SymbolEqualityComparer.Default.Equals(definition, known.StreamRequestHandler) ? HandlerKind.Stream
            : HandlerKind.Notification;

        var message = handlerInterface.TypeArguments[0];

        if (message is not INamedTypeSymbol)
        {
            return null;
        }

        var response = handlerInterface.TypeArguments.Length > 1 ? handlerInterface.TypeArguments[1] : null;

        return new HandlerInfo(
            kind,
            handlerType,
            handlerName,
            message.ToDisplayString(Qualified),
            message.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(Pattern),
            message.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            message.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(),
            response?.ToDisplayString(Qualified),
            response?.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(Pattern),
            message.IsSealed || message.IsValueType,
            known.IsVoidRequest(message),
            SuperTypes(message).ToEquatableArray(),
            exportAccessor,
            isExportable && IsPublic(message) && (response is null || IsPublic(response)),
            location);
    }

    private static IEnumerable<string> SuperTypes(ITypeSymbol type)
    {
        for (var current = type.BaseType; current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); current = current.BaseType)
        {
            yield return current.ToDisplayString(Pattern);
        }

        foreach (var @interface in type.AllInterfaces)
        {
            yield return @interface.ToDisplayString(Pattern);
        }
    }

    /// <summary>Visible to generated code in the same project: nothing in the chain is private, protected or file-local.</summary>
    private static bool IsVisibleInAssembly(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal
                || current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Visible to other projects: public all the way out, generic arguments included.</summary>
    private static bool IsPublic(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return IsPublic(array.ElementType);
            case ITypeParameterSymbol:
                return false;
            case INamedTypeSymbol named:
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    if (current.DeclaredAccessibility != Accessibility.Public)
                    {
                        return false;
                    }
                }

                return named.TypeArguments.All(IsPublic);
            default:
                return false;
        }
    }

    /// <summary>MediatingKit's contracts, as symbols of the compilation at hand.</summary>
    private sealed class KnownTypes
    {
        private KnownTypes(Compilation compilation)
        {
            Request = compilation.GetTypeByMetadataName("MediatingKit.IRequest`1")!;
            VoidRequest = compilation.GetTypeByMetadataName("MediatingKit.IRequest")!;
            StreamRequest = compilation.GetTypeByMetadataName("MediatingKit.IStreamRequest`1")!;
            RequestHandler = compilation.GetTypeByMetadataName("MediatingKit.IRequestHandler`2")!;
            VoidRequestHandler = compilation.GetTypeByMetadataName("MediatingKit.IRequestHandler`1")!;
            StreamRequestHandler = compilation.GetTypeByMetadataName("MediatingKit.IStreamRequestHandler`2")!;
            NotificationHandler = compilation.GetTypeByMetadataName("MediatingKit.INotificationHandler`1")!;
        }

        public INamedTypeSymbol Request { get; }

        public INamedTypeSymbol VoidRequest { get; }

        public INamedTypeSymbol StreamRequest { get; }

        public INamedTypeSymbol RequestHandler { get; }

        public INamedTypeSymbol VoidRequestHandler { get; }

        public INamedTypeSymbol StreamRequestHandler { get; }

        public INamedTypeSymbol NotificationHandler { get; }

        public static KnownTypes? Create(Compilation compilation)
        {
            var known = new KnownTypes(compilation);

            return known.Request is null || known.VoidRequest is null || known.StreamRequest is null || known.RequestHandler is null
                || known.VoidRequestHandler is null || known.StreamRequestHandler is null || known.NotificationHandler is null
                ? null
                : known;
        }

        public bool IsHandlerInterface(INamedTypeSymbol type)
        {
            var definition = type.OriginalDefinition;

            return SymbolEqualityComparer.Default.Equals(definition, RequestHandler)
                || SymbolEqualityComparer.Default.Equals(definition, VoidRequestHandler)
                || SymbolEqualityComparer.Default.Equals(definition, StreamRequestHandler)
                || SymbolEqualityComparer.Default.Equals(definition, NotificationHandler);
        }

        /// <summary>The generic request interfaces a type implements; <c>IRequest</c> shows up as <c>IRequest&lt;Unit&gt;</c>.</summary>
        public ImmutableArray<INamedTypeSymbol> RequestInterfaces(ITypeSymbol type) =>
            type.AllInterfaces
                .Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, Request)
                    || SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, StreamRequest))
                .ToImmutableArray();

        public bool IsVoidRequest(ITypeSymbol type) =>
            type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, VoidRequest));
    }
}
