namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents an XML CDATA section node.</summary>
/// <example>
/// <code>
/// var cdata = SyntaxFactory.CDataSection("raw &lt;content&gt;");
/// var updated = cdata.WithText("other");
/// </code>
/// </example>
public sealed class XmlCDataSectionSyntax : XmlSyntaxNode
{
    private const string OpeningDelimiter = "<![CDATA[";

    public XmlCDataSectionSyntax(string text, string fullText, int fullStart = 0)
        : base(XmlSyntaxKind.XmlCDataSection, fullText, fullStart, [new XmlSyntaxToken(XmlSyntaxKind.CDataToken, text, fullStart: fullStart + GetTextOffset(fullText))])
    {
        Text = text;
    }

    /// <summary>The offset of the section text inside <paramref name="fullText"/>, which the parser always opens with <c>&lt;![CDATA[</c>.</summary>
    private static int GetTextOffset(string fullText)
        => fullText.StartsWith(OpeningDelimiter, StringComparison.Ordinal) ? OpeningDelimiter.Length : 0;

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
