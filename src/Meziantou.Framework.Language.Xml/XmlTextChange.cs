using System.Runtime.InteropServices;

namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// Represents a text replacement operation applied to a <see cref="SourceText"/> or <see cref="XmlSyntaxTree"/>.
/// </summary>
/// <example>
/// <code>
/// var change = new XmlTextChange(new TextSpan(10, 5), "2.0.0");
/// var updated = tree.WithChanges(change);
/// </code>
/// </example>
[StructLayout(LayoutKind.Auto)]
public readonly struct XmlTextChange
{
    public XmlTextChange(TextSpan span, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        Span = span;
        NewText = newText;
    }

    public TextSpan Span { get; }
    public string NewText { get; }
}
