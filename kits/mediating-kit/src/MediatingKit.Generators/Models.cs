using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MediatingKit.Generators;

internal enum HandlerKind
{
    Request,
    VoidRequest,
    Stream,
    Notification,
}

/// <summary>A source location without the syntax tree, so models stay cheap to compare and hold.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location?.SourceTree is null ? null : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
}

internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, [.. Arguments]);
}

/// <summary>One handler interface implemented by one handler class.</summary>
/// <param name="Kind">What the handler handles.</param>
/// <param name="HandlerType">The handler's concrete type, or the handler interface for one exported by a referenced project.</param>
/// <param name="HandlerName">The handler's name, for diagnostics.</param>
/// <param name="MessageType">The message type as the handler interface names it.</param>
/// <param name="MessagePattern">The message type without nullable annotations, for <c>is</c> and <c>typeof</c>.</param>
/// <param name="MessageName">The message type's short name, for span names.</param>
/// <param name="MessageFullName">The message type's full name, for the telemetry attribute.</param>
/// <param name="ResponseType">The response or item type as the handler interface names it.</param>
/// <param name="ResponsePattern">The response type without nullable annotations, for <c>typeof</c>.</param>
/// <param name="MessageIsExact">Whether the message type is sealed or a struct, so a type check pins the response type.</param>
/// <param name="MessageIsVoidRequest">Whether the message type implements the non-generic <c>IRequest</c>.</param>
/// <param name="MessageSuperTypes">Every base type and interface of the message type.</param>
/// <param name="ExportAccessor">For a handler exported by a referenced project, the static method that resolves it.</param>
/// <param name="IsExportable">Whether a referencing project could dispatch to this handler: its message and response are public.</param>
/// <param name="Location">Where the handler is declared, for diagnostics.</param>
internal sealed record HandlerInfo(
    HandlerKind Kind,
    string HandlerType,
    string HandlerName,
    string MessageType,
    string MessagePattern,
    string MessageName,
    string MessageFullName,
    string? ResponseType,
    string? ResponsePattern,
    bool MessageIsExact,
    bool MessageIsVoidRequest,
    EquatableArray<string> MessageSuperTypes,
    string? ExportAccessor,
    bool IsExportable,
    LocationInfo? Location)
{
    public bool IsRequestKind => Kind is HandlerKind.Request or HandlerKind.VoidRequest;

    /// <summary>More base types and interfaces means more derived, so checking these first keeps the most specific match.</summary>
    public int Depth => MessageSuperTypes.Length;

    /// <summary>The handler interface, for direct calls and exported signatures.</summary>
    public string InterfaceType => Kind switch
    {
        HandlerKind.Request => $"global::MediatingKit.IRequestHandler<{MessageType}, {ResponseType}>",
        HandlerKind.VoidRequest => $"global::MediatingKit.IRequestHandler<{MessageType}>",
        HandlerKind.Stream => $"global::MediatingKit.IStreamRequestHandler<{MessageType}, {ResponseType}>",
        _ => $"global::MediatingKit.INotificationHandler<{MessageType}>",
    };
}

/// <summary>A class, struct or record declared in this project that takes part in mediation.</summary>
/// <param name="FullType">The type, fully qualified.</param>
/// <param name="Name">The type's name, for diagnostics.</param>
/// <param name="Location">Where it is declared.</param>
/// <param name="RequestHint">For a concrete request, the handler interface to suggest when it has none.</param>
/// <param name="IsStreamRequest">Whether the request is a stream request.</param>
/// <param name="SuperTypes">Every base type and interface.</param>
/// <param name="IsHandler">Whether it is a concrete handler class, usable or not.</param>
/// <param name="Handlers">The handler interfaces it implements, if it is a usable handler.</param>
/// <param name="Diagnostics">Problems found with the type itself.</param>
internal sealed record DeclaredType(
    string FullType,
    string Name,
    LocationInfo? Location,
    string? RequestHint,
    bool IsStreamRequest,
    EquatableArray<string> SuperTypes,
    bool IsHandler,
    EquatableArray<HandlerInfo> Handlers,
    EquatableArray<DiagnosticInfo> Diagnostics);

/// <summary>An <c>AddMediator()</c> call to replace.</summary>
internal sealed record InterceptedCall(int Version, string Data, string DisplayLocation);

/// <summary>What the referenced projects contribute.</summary>
/// <param name="IsMediatingKitReferenced">Whether this compilation references MediatingKit at all.</param>
/// <param name="AssemblyName">This compilation's assembly name, for the exports class name.</param>
/// <param name="ExportClasses">The exports classes of referenced projects, whose registration this project's mediator calls.</param>
/// <param name="Handlers">The handlers those projects export.</param>
internal sealed record ReferencedHandlers(
    bool IsMediatingKitReferenced,
    string AssemblyName,
    EquatableArray<string> ExportClasses,
    EquatableArray<HandlerInfo> Handlers)
{
    public static readonly ReferencedHandlers None = new(false, string.Empty, EquatableArray<string>.Empty, EquatableArray<HandlerInfo>.Empty);
}
