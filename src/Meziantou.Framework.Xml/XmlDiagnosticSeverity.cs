namespace Meziantou.Framework.Xml;

/// <summary>
/// Indicates the severity level of an <see cref="XmlDiagnostic"/>.
/// </summary>
/// <example>
/// <code>
/// if (diagnostic.Severity == XmlDiagnosticSeverity.Error)
/// {
///     // stop processing
/// }
/// </code>
/// </example>
public enum XmlDiagnosticSeverity
{
    Hidden,
    Info,
    Warning,
    Error,
}
