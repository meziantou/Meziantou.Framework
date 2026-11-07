using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>The opening tag of an element, with the attributes it declares.</summary>
public sealed class XmlElementStartTagSyntax : XmlSyntaxNode
{
    private SyntaxNode? _attributes;

    internal XmlElementStartTagSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken LessThanToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxList<XmlAttributeSyntax> Attributes => new(GetRed(ref _attributes, 2));
    public SyntaxToken GreaterThanToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the name of the element the tag opens.</summary>
    public string Name => NameToken.Text;

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlElementStartTagSyntax Update(SyntaxToken lessThanToken, SyntaxToken nameToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken greaterThanToken)
    {
        if (lessThanToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && attributes.Green == Green.GetSlot(2) && greaterThanToken.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.XmlElementStartTag(lessThanToken, nameToken, attributes, greaterThanToken).WithAnnotationsFrom(this);
    }

    public XmlElementStartTagSyntax WithLessThanToken(SyntaxToken lessThanToken) => Update(lessThanToken, NameToken, Attributes, GreaterThanToken);
    public XmlElementStartTagSyntax WithNameToken(SyntaxToken nameToken) => Update(LessThanToken, nameToken, Attributes, GreaterThanToken);
    public XmlElementStartTagSyntax WithAttributes(SyntaxList<XmlAttributeSyntax> attributes) => Update(LessThanToken, NameToken, attributes, GreaterThanToken);
    public XmlElementStartTagSyntax WithGreaterThanToken(SyntaxToken greaterThanToken) => Update(LessThanToken, NameToken, Attributes, greaterThanToken);

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

        visitor.VisitElementStartTag(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitElementStartTag(this);
    }
}
