namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a parser diagnostic produced while reading JSON syntax.</summary>
public sealed record JsonDiagnostic(string Id, string Message, JsonDiagnosticSeverity Severity, TextSpan Span);
