using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>An element written with a start tag and an end tag, and whatever stands between them.</summary>
public sealed class XmlElementSyntax : XmlNodeSyntax
{
    private SyntaxNode? _startTag;
    private SyntaxNode? _content;
    private SyntaxNode? _endTag;

    internal XmlElementSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public XmlElementStartTagSyntax StartTag => (XmlElementStartTagSyntax)GetRed(ref _startTag, 0)!;
    public SyntaxList<XmlNodeSyntax> Content => new(GetRed(ref _content, 1));
    public XmlElementEndTagSyntax? EndTag => (XmlElementEndTagSyntax?)GetRed(ref _endTag, 2);

    /// <summary>Gets the name the start tag declares.</summary>
    public string Name => StartTag.NameToken.Text;

    /// <summary>Gets the attributes the start tag declares.</summary>
    public SyntaxList<XmlAttributeSyntax> Attributes => StartTag.Attributes;

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

    /// <summary>Gets the text between the two tags, exactly as it was written.</summary>
    public string GetInnerText() => Content.ToFullString();

    /// <summary>Returns this element with <paramref name="text"/> as its only content.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public XmlElementSyntax WithInnerText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(GetInnerText(), text, StringComparison.Ordinal))
            return this;

        return WithContent(new SyntaxList<XmlNodeSyntax>(SyntaxFactory.XmlText(text)));
    }

    /// <summary>Returns this element renamed, in both of its tags.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlElementSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        var startTag = StartTag.WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(StartTag.NameToken));
        var endTag = EndTag?.WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(EndTag.NameToken));

        return Update(startTag, Content, endTag);
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlElementSyntax Update(XmlElementStartTagSyntax startTag, SyntaxList<XmlNodeSyntax> content, XmlElementEndTagSyntax? endTag)
    {
        if (ReferenceEquals(startTag.Green, Green.GetSlot(0)) && content.Green == Green.GetSlot(1) && ReferenceEquals(endTag?.Green, Green.GetSlot(2)))
            return this;

        return SyntaxFactory.XmlElement(startTag, content, endTag).WithAnnotationsFrom(this);
    }

    public XmlElementSyntax WithStartTag(XmlElementStartTagSyntax startTag) => Update(startTag, Content, EndTag);
    public XmlElementSyntax WithContent(SyntaxList<XmlNodeSyntax> content) => Update(StartTag, content, EndTag);
    public XmlElementSyntax WithEndTag(XmlElementEndTagSyntax? endTag) => Update(StartTag, Content, endTag);

    internal override SyntaxNode? GetNodeSlot(int index) => index switch
{
        0 => GetRed(ref _startTag, 0),
        1 => GetRed(ref _content, 1),
        2 => GetRed(ref _endTag, 2),
        _ => null,
    };

    internal override SyntaxNode? GetCachedSlot(int index) => index switch
{
        0 => _startTag,
        1 => _content,
        2 => _endTag,
        _ => null,
    };

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitElement(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitElement(this);
    }
}
