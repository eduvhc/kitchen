using System.ComponentModel;

namespace MediatingKit;

/// <summary>
/// Written by the source generator into every project with handlers. It names a generated class that registers the
/// project's handlers and resolves them by interface, so the mediator generated for a project that references this
/// one can dispatch to them even when they are internal. Not for direct use.
/// </summary>
/// <param name="exportsType">The generated class.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MediatorExportsAttribute(Type exportsType) : Attribute
{
    /// <summary>The generated class.</summary>
    public Type ExportsType { get; } = exportsType;
}
