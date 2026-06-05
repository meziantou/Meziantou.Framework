namespace Meziantou.Framework.Xml;

/// <summary>Represents an XML CDATA section node.</summary>
/// <example>
/// <code>
/// var cdata = SyntaxFactory.CDataSection("raw &lt;content&gt;");
/// var updated = cdata.WithText("other");
/// </code>
/// </example>
public sealed class XmlCDataSectionSyntax : XmlSyntaxNode
{
    public XmlCDataSectionSyntax(string text, string fullText)
        : base(XmlSyntaxKind.XmlCDataSection, fullText, [new XmlSyntaxToken(XmlSyntaxKind.CDataToken, text)])
    {
        Text = text;
    }

    public string Text { get; }

    public XmlCDataSectionSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.CDataSection(text);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitCDataSection(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitCDataSection(this);
}
