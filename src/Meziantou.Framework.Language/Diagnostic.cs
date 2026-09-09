namespace Meziantou.Framework.Language;

/// <summary>Represents a diagnostic produced while reading source text.</summary>
/// <example>
/// <code>
/// foreach (var diagnostic in tree.Diagnostics)
/// {
///     Console.WriteLine($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
/// }
/// </code>
/// </example>
public sealed record Diagnostic(string Id, string Message, DiagnosticSeverity Severity, Location Location);
