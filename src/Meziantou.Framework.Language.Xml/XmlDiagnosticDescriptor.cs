namespace Meziantou.Framework.Language.Xml;

/// <summary>Describes a diagnostic definition (id, title, message template, and default severity).</summary>
/// <example>
/// <code>
/// var descriptor = new XmlDiagnosticDescriptor("XML0001", "Missing end tag", "Missing end tag for '{0}'", XmlDiagnosticSeverity.Error);
/// </code>
/// </example>
public sealed class XmlDiagnosticDescriptor
{
    public XmlDiagnosticDescriptor(string id, string title, string messageFormat, XmlDiagnosticSeverity defaultSeverity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(messageFormat);

        Id = id;
        Title = title;
        MessageFormat = messageFormat;
        DefaultSeverity = defaultSeverity;
    }

    public string Id { get; }
    public string Title { get; }
    public string MessageFormat { get; }
    public XmlDiagnosticSeverity DefaultSeverity { get; }
}
