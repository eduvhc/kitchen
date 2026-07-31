; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
MK1001 | MessagingKit | Error | Two message types share one wire name
MK1002 | MessagingKit | Warning | Message handler is never registered
MK1003 | MessagingKit | Error | Message name is empty
MK1004 | MessagingKit | Disabled | Message name is derived from the type name
