using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Text the parser could make nothing of, kept so the document still reproduces its source.</summary>
public sealed class XmlSkippedTextSyntax : XmlNodeSyntax
{
    internal XmlSkippedTextSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxTokenList Tokens => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the text the parser could make nothing of.</summary>
    public string Text => ToString();

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlSkippedTextSyntax Update(SyntaxTokenList tokens)
    {
        if (tokens.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.XmlSkippedText(tokens).WithAnnotationsFrom(this);
    }

    public XmlSkippedTextSyntax WithTokens(SyntaxTokenList tokens) => Update(tokens);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitSkippedText(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitSkippedText(this);
    }
}
