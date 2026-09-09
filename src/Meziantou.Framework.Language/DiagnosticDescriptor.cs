namespace Meziantou.Framework.Language;

/// <summary>Describes a diagnostic definition: its id, title, message template, and default severity.</summary>
/// <example>
/// <code>
/// var descriptor = new DiagnosticDescriptor("XML0001", "Missing end tag", "Missing end tag for '{0}'.", DiagnosticSeverity.Error);
/// </code>
/// </example>
public sealed class DiagnosticDescriptor
{
    public DiagnosticDescriptor(string id, string title, string messageFormat, DiagnosticSeverity defaultSeverity)
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
    public DiagnosticSeverity DefaultSeverity { get; }
}
