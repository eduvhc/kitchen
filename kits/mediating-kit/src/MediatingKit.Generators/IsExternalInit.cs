namespace System.Runtime.CompilerServices;

/// <summary>
/// Records and init-only setters need this type, which netstandard2.0 does not ship. The compiler
/// only requires it to exist.
/// </summary>
internal static class IsExternalInit;
