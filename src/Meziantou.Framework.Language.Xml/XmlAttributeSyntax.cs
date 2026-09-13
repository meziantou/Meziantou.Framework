using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>An attribute of a tag: a name, an equals sign, and a quoted value.</summary>
public sealed class XmlAttributeSyntax : XmlSyntaxNode
{
    internal XmlAttributeSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken NameToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken EqualsToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken StartQuoteToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));
    public SyntaxToken ValueToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));
    public SyntaxToken EndQuoteToken => new(this, Green.GetSlot(4), GetChildPosition(4), GetChildIndex(4));

    /// <summary>Gets the name of the attribute.</summary>
    public string Name => NameToken.Text;

    /// <summary>Gets the value of the attribute as an XML processor reads it.</summary>
    /// <remarks>
    /// Character references and the predefined entities are resolved, and each tab and line break becomes a space
    /// (XML 1.0 §3.3.3). A reference to any other entity is left as written.
    /// </remarks>
    public string Value => ValueToken.ValueText;

    /// <summary>Returns this attribute renamed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlAttributeSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, Name, StringComparison.Ordinal) ? this : WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(NameToken));
    }

    /// <summary>Returns this attribute carrying a different value, escaped for the quote character it is written with.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public XmlAttributeSyntax WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        return WithValueToken(SyntaxFactory.AttributeValue(value, StartQuoteToken.Text.Length == 1 ? StartQuoteToken.Text[0] : '"').WithTriviaFrom(ValueToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlAttributeSyntax Update(SyntaxToken nameToken, SyntaxToken equalsToken, SyntaxToken startQuoteToken, SyntaxToken valueToken, SyntaxToken endQuoteToken)
    {
        if (nameToken.Node == Green.GetSlot(0) && equalsToken.Node == Green.GetSlot(1) && startQuoteToken.Node == Green.GetSlot(2) && valueToken.Node == Green.GetSlot(3) && endQuoteToken.Node == Green.GetSlot(4))
            return this;

        return SyntaxFactory.XmlAttribute(nameToken, equalsToken, startQuoteToken, valueToken, endQuoteToken).WithAnnotationsFrom(this);
    }

    public XmlAttributeSyntax WithNameToken(SyntaxToken nameToken) => Update(nameToken, EqualsToken, StartQuoteToken, ValueToken, EndQuoteToken);
    public XmlAttributeSyntax WithEqualsToken(SyntaxToken equalsToken) => Update(NameToken, equalsToken, StartQuoteToken, ValueToken, EndQuoteToken);
    public XmlAttributeSyntax WithStartQuoteToken(SyntaxToken startQuoteToken) => Update(NameToken, EqualsToken, startQuoteToken, ValueToken, EndQuoteToken);
    public XmlAttributeSyntax WithValueToken(SyntaxToken valueToken) => Update(NameToken, EqualsToken, StartQuoteToken, valueToken, EndQuoteToken);
    public XmlAttributeSyntax WithEndQuoteToken(SyntaxToken endQuoteToken) => Update(NameToken, EqualsToken, StartQuoteToken, ValueToken, endQuoteToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitAttribute(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitAttribute(this);
    }
}
