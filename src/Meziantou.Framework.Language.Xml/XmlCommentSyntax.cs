namespace Meziantou.Framework.Language.Xml;

/// <summary>Represents an XML comment node.</summary>
/// <example>
/// <code>
/// var comment = SyntaxFactory.Comment("generated");
/// var updated = comment.WithText("updated");
/// </code>
/// </example>
public sealed class XmlCommentSyntax : XmlSyntaxNode
{
    private const string OpeningDelimiter = "<!--";

    public XmlCommentSyntax(string text, string fullText, int fullStart = 0)
        : base(XmlSyntaxKind.XmlComment, fullText, fullStart, [new XmlSyntaxToken(XmlSyntaxKind.CommentToken, text, fullStart: fullStart + GetTextOffset(fullText))])
    {
        Text = text;
    }

    /// <summary>The offset of the comment text inside <paramref name="fullText"/>, which the parser always opens with <c>&lt;!--</c>.</summary>
    private static int GetTextOffset(string fullText)
        => fullText.StartsWith(OpeningDelimiter, StringComparison.Ordinal) ? OpeningDelimiter.Length : 0;

    public string Text { get; }

    public XmlCommentSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.Comment(text);
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitComment(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitComment(this);
}
