using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>An element written as one self-closing tag.</summary>
public sealed class XmlEmptyElementSyntax : XmlNodeSyntax
{
    private SyntaxNode? _attributes;

    internal XmlEmptyElementSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken LessThanToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxList<XmlAttributeSyntax> Attributes => new(GetRed(ref _attributes, 2));
    public SyntaxToken SlashGreaterThanToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the name of the element.</summary>
    public string Name => NameToken.Text;

    /// <summary>Gets the first attribute called <paramref name="name"/>, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlAttributeSyntax? GetAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        foreach (var attribute in Attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
                return attribute;
        }

        return null;
    }

    /// <summary>Returns this element renamed.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlEmptyElementSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, Name, StringComparison.Ordinal) ? this : WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(NameToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlEmptyElementSyntax Update(SyntaxToken lessThanToken, SyntaxToken nameToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken slashGreaterThanToken)
    {
        if (lessThanToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && attributes.Green == Green.GetSlot(2) && slashGreaterThanToken.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.XmlEmptyElement(lessThanToken, nameToken, attributes, slashGreaterThanToken).WithAnnotationsFrom(this);
    }

    public XmlEmptyElementSyntax WithLessThanToken(SyntaxToken lessThanToken) => Update(lessThanToken, NameToken, Attributes, SlashGreaterThanToken);
    public XmlEmptyElementSyntax WithNameToken(SyntaxToken nameToken) => Update(LessThanToken, nameToken, Attributes, SlashGreaterThanToken);
    public XmlEmptyElementSyntax WithAttributes(SyntaxList<XmlAttributeSyntax> attributes) => Update(LessThanToken, NameToken, attributes, SlashGreaterThanToken);
    public XmlEmptyElementSyntax WithSlashGreaterThanToken(SyntaxToken slashGreaterThanToken) => Update(LessThanToken, NameToken, Attributes, slashGreaterThanToken);

    internal override SyntaxNode? GetNodeSlot(int index) => index switch
{
        2 => GetRed(ref _attributes, 2),
        _ => null,
    };

    internal override SyntaxNode? GetCachedSlot(int index) => index switch
{
        2 => _attributes,
        _ => null,
    };

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitEmptyElement(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitEmptyElement(this);
    }
}
