using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A comment.</summary>
public sealed class XmlCommentSyntax : XmlNodeSyntax
{
    internal XmlCommentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StartCommentToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken TextToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken EndCommentToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the text between the comment delimiters.</summary>
    public string Text => TextToken.Text;

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public XmlCommentSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Equals(text, Text, StringComparison.Ordinal) ? this : WithTextToken(SyntaxFactory.Token(SyntaxKind.CommentToken, text).WithTriviaFrom(TextToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlCommentSyntax Update(SyntaxToken startCommentToken, SyntaxToken textToken, SyntaxToken endCommentToken)
    {
        if (startCommentToken.Node == Green.GetSlot(0) && textToken.Node == Green.GetSlot(1) && endCommentToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.XmlComment(startCommentToken, textToken, endCommentToken).WithAnnotationsFrom(this);
    }

    public XmlCommentSyntax WithStartCommentToken(SyntaxToken startCommentToken) => Update(startCommentToken, TextToken, EndCommentToken);
    public XmlCommentSyntax WithTextToken(SyntaxToken textToken) => Update(StartCommentToken, textToken, EndCommentToken);
    public XmlCommentSyntax WithEndCommentToken(SyntaxToken endCommentToken) => Update(StartCommentToken, TextToken, endCommentToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitComment(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitComment(this);
    }
}
