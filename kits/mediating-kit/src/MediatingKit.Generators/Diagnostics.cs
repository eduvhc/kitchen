using Microsoft.CodeAnalysis;

namespace MediatingKit.Generators;

internal static class Diagnostics
{
    private const string Category = "MediatingKit";

    /// <summary>
    /// A request nothing handles fails on every send. Only checked in projects that have handlers of their own, so a
    /// contracts project whose requests are handled elsewhere is not flagged.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingHandler = new(
        id: "MED1001",
        title: "Request has no handler",
        messageFormat: "'{0}' has no handler; add a class implementing {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "Every request needs exactly one handler, in this project or in a project it references. If the handler "
            + "lives in a project this one cannot see, set this rule's severity to none for this project.");

    /// <summary>Two handlers for one request leaves the mediator nothing correct to call.</summary>
    public static readonly DiagnosticDescriptor DuplicateHandler = new(
        id: "MED1002",
        title: "Request has more than one handler",
        messageFormat: "'{0}' is handled by more than one handler: {1}; a request has exactly one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Remove all but one handler. To fan out to several handlers, publish a notification instead.");

    /// <summary>The response type is what Send infers, so a type may declare only one.</summary>
    public static readonly DiagnosticDescriptor AmbiguousResponse = new(
        id: "MED1003",
        title: "Type declares more than one response",
        messageFormat: "'{0}' implements {1}; a request declares exactly one response type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Implement one of IRequest, IRequest<TResponse> or IStreamRequest<TResponse>, once.");

    /// <summary>The generated mediator has to name the handler, so it must be closed and visible to it.</summary>
    public static readonly DiagnosticDescriptor UnusableHandler = new(
        id: "MED1004",
        title: "Handler cannot be dispatched to",
        messageFormat: "'{0}' cannot be a handler: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:
            "The generated mediator constructs and calls handlers by name, so a handler must be a non-generic class "
            + "visible to the rest of its project: public or internal, not private, protected or file-local.");
}
