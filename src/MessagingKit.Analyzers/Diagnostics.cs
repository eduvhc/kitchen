using Microsoft.CodeAnalysis;

namespace MessagingKit.Analyzers;

internal static class Diagnostics
{
    private const string Category = "MessagingKit";

    /// <summary>
    /// Two message types resolving to one wire name means the receiver deserializes into whichever
    /// the registry saw last. Silent at runtime, so it is an error here.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateMessageName = new(
        id: "MK1001",
        title: "Two message types share one wire name",
        messageFormat: "Message name '{0}' is used by both '{1}' and '{2}'; the receiver cannot tell them apart",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Message names must be unique. Apply [Message(\"...\")] to give one of them a distinct name.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    /// A handler nothing registers never runs. Warning rather than error because the registration may
    /// legitimately live in another project.
    /// </summary>
    public static readonly DiagnosticDescriptor HandlerNeverRegistered = new(
        id: "MK1002",
        title: "Message handler is never registered",
        messageFormat: "'{0}' handles '{1}' but is never registered; it will never run",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Register the handler with AddMessageHandler<TMessage, THandler>() or Handles<TMessage, THandler>(). "
            + "If the registration lives in another project, suppress this there.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>An empty name would throw when the attribute is constructed at startup.</summary>
    public static readonly DiagnosticDescriptor EmptyMessageName = new(
        id: "MK1003",
        title: "Message name is empty",
        messageFormat: "[Message] requires a non-empty name",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An empty name throws when the attribute is read during startup.");

    /// <summary>
    /// Off by default: correct for new work, worth turning on once messages are in production and a
    /// class rename would orphan queued rows.
    /// </summary>
    public static readonly DiagnosticDescriptor MessageNameNotPinned = new(
        id: "MK1004",
        title: "Message name is derived from the type name",
        messageFormat: "'{0}' has no [Message] attribute, so renaming the type renames the message on the wire",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        description:
            "Without [Message(\"...\")] the wire name follows the type name. Renaming or moving the type "
            + "then orphans messages already queued under the old name.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
