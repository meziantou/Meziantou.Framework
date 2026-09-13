using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A run of character data, which in XML includes the whitespace between tags.</summary>
public sealed class XmlTextSyntax : XmlNodeSyntax
{
    internal XmlTextSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken TextToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the character data, exactly as it was written.</summary>
    public string Text => TextToken.Text;

    /// <summary>Gets the character data as XML reads it: references resolved and line breaks normalized.</summary>
    public string Value => TextToken.ValueText;

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public XmlTextSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Equals(text, Text, StringComparison.Ordinal) ? this : WithTextToken(SyntaxFactory.TextToken(text).WithTriviaFrom(TextToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlTextSyntax Update(SyntaxToken textToken)
    {
        if (textToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.XmlText(textToken).WithAnnotationsFrom(this);
    }

    public XmlTextSyntax WithTextToken(SyntaxToken textToken) => Update(textToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitText(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitText(this);
    }
}
