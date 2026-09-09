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
        : base(XmlSyntaxKind.XmlCDataSection, fullText, [new XmlSyntaxToken(XmlSyntaxKind.CDataToken, text, fullStart: GetTextStart(fullText, fullStart))], fullStart)
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

    /// <summary>Returns where the section text starts, skipping the opening delimiter when the node carries one.</summary>
    private static int GetTextStart(string fullText, int fullStart)
    {
        return fullText.StartsWith(OpeningDelimiter, StringComparison.Ordinal) ? fullStart + OpeningDelimiter.Length : fullStart;
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitCDataSection(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitCDataSection(this);
}
