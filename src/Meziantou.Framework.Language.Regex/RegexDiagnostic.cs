namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a parser diagnostic produced while reading a regular-expression pattern.</summary>
public sealed record RegexDiagnostic(string Id, string Message, RegexDiagnosticSeverity Severity, TextSpan Span);
