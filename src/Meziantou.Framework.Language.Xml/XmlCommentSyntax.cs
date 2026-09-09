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
        : base(XmlSyntaxKind.XmlComment, fullText, [new XmlSyntaxToken(XmlSyntaxKind.CommentToken, text, fullStart: GetTextStart(fullText, fullStart))], fullStart)
    {
        Text = text;
    }

    public string Text { get; }

    public XmlCommentSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.Comment(text);
    }

    /// <summary>Returns where the comment text starts, skipping the opening delimiter when the node carries one.</summary>
    private static int GetTextStart(string fullText, int fullStart)
    {
        return fullText.StartsWith(OpeningDelimiter, StringComparison.Ordinal) ? fullStart + OpeningDelimiter.Length : fullStart;
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitComment(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitComment(this);
}
