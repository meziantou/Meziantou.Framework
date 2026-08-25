namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parser diagnostic produced while reading shell syntax.</summary>
public sealed record ShellDiagnostic(string Id, string Message, ShellDiagnosticSeverity Severity, TextSpan Span);
