namespace Meziantou.Framework.Language;

/// <summary>Represents a diagnostic produced while reading source text.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Language.Xml.Tests/XmlSyntaxTreeTests.cs" ref:region="Diagnostic_GetDiagnostics" ref:dedent="true">
/// var tree = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;child&gt;&lt;/root&gt;");
///
/// var messages = new List&lt;string&gt;();
/// foreach (var diagnostic in tree.GetDiagnostics())
/// {
///     messages.Add($"{diagnostic.Id} at {diagnostic.Location}: {diagnostic.Message}");
/// }
///
/// Assert.Equal(["XML0001 at (0,6)-(0,13): Missing end tag for 'child'."], messages);
/// </code>
/// </example>
public sealed record Diagnostic(string Id, string Message, DiagnosticSeverity Severity, Location Location)
{
    /// <summary>Creates a diagnostic from a descriptor, formatting <see cref="DiagnosticDescriptor.MessageFormat"/> with <paramref name="arguments"/>.</summary>
    /// <param name="descriptor">The descriptor supplying the id, message template, and severity.</param>
    /// <param name="location">Where the diagnostic applies.</param>
    /// <param name="arguments">The values to substitute into the message template. The template is used verbatim when there is none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> or <paramref name="location"/> is <see langword="null"/>.</exception>
    public static Diagnostic Create(DiagnosticDescriptor descriptor, Location location, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(location);

        return new Diagnostic(descriptor.Id, FormatMessage(descriptor.MessageFormat, arguments), descriptor.DefaultSeverity, location);
    }

    internal static string FormatMessage(string messageFormat, object?[]? arguments)
    {
        // A template with no arguments is used verbatim, so a message containing a literal brace does not have to escape it.
        if (arguments is null || arguments.Length == 0)
            return messageFormat;

        return string.Format(CultureInfo.InvariantCulture, messageFormat, arguments);
    }
}
