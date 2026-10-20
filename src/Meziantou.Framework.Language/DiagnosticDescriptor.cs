namespace Meziantou.Framework.Language;

/// <summary>Describes a diagnostic definition: its id, title, message template, and default severity.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Tests/DiagnosticDescriptorTests.cs" ref:region="DiagnosticDescriptor_Create" ref:dedent="true">
/// var descriptor = new DiagnosticDescriptor("XML0001", "Missing end tag", "Missing end tag for '{0}'.", DiagnosticSeverity.Error);
///
/// // Create a diagnostic from the descriptor: the arguments fill the message template
/// var diagnostic = Diagnostic.Create(descriptor, Location.None, "root");
/// Assert.Equal("XML0001", diagnostic.Id);
/// Assert.Equal("Missing end tag for 'root'.", diagnostic.Message);
/// Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
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
