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
    public XmlCommentSyntax(string text, string fullText)
        : base(XmlSyntaxKind.XmlComment, fullText, [new XmlSyntaxToken(XmlSyntaxKind.CommentToken, text)])
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

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitComment(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitComment(this);
}
