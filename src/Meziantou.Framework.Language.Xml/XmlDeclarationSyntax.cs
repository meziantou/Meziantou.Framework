using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>The XML declaration, whose version, encoding and standalone are written as attributes.</summary>
public sealed class XmlDeclarationSyntax : XmlNodeSyntax
{
    private SyntaxNode? _attributes;

    internal XmlDeclarationSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StartDeclarationToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxList<XmlAttributeSyntax> Attributes => new(GetRed(ref _attributes, 1));
    public SyntaxToken EndDeclarationToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the version the declaration states, or <c>1.0</c> when it states none.</summary>
    public string Version => GetAttribute("version")?.Value ?? "1.0";

    /// <summary>Gets the encoding the declaration states, or <see langword="null"/> when it states none.</summary>
    public string? Encoding => GetAttribute("encoding")?.Value;

    /// <summary>Gets what the declaration states for standalone, or <see langword="null"/> when it states nothing.</summary>
    public string? Standalone => GetAttribute("standalone")?.Value;

    public XmlAttributeSyntax? VersionAttribute => GetAttribute("version");
    public XmlAttributeSyntax? EncodingAttribute => GetAttribute("encoding");
    public XmlAttributeSyntax? StandaloneAttribute => GetAttribute("standalone");

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

    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public XmlDeclarationSyntax WithVersion(string version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return SetAttribute("version", version);
    }

    public XmlDeclarationSyntax WithEncoding(string? encoding) => SetAttribute("encoding", encoding);

    public XmlDeclarationSyntax WithStandalone(string? standalone) => SetAttribute("standalone", standalone);

    /// <summary>
    /// Sets, adds, or removes one pseudo-attribute, leaving the rest of the declaration exactly as it was written.
    /// </summary>
    /// <remarks>
    /// An attribute that is already there keeps its position and its spacing; a new one is added at the end, in front
    /// of whatever whitespace stands before the <c>?&gt;</c>.
    /// </remarks>
    private XmlDeclarationSyntax SetAttribute(string name, string? value)
    {
        var existing = GetAttribute(name);
        if (existing is not null)
        {
            if (value is null)
                return WithAttributes(Attributes.Remove(existing));

            return WithAttributes(Attributes.Replace(existing, existing.WithValue(value)));
        }

        if (value is null)
            return this;

        return WithAttributes(Attributes.Add(SyntaxFactory.XmlAttribute(name, value).WithLeadingTrivia(SyntaxFactory.Space)));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlDeclarationSyntax Update(SyntaxToken startDeclarationToken, SyntaxList<XmlAttributeSyntax> attributes, SyntaxToken endDeclarationToken)
    {
        if (startDeclarationToken.Node == Green.GetSlot(0) && attributes.Green == Green.GetSlot(1) && endDeclarationToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.XmlDeclaration(startDeclarationToken, attributes, endDeclarationToken).WithAnnotationsFrom(this);
    }

    public XmlDeclarationSyntax WithStartDeclarationToken(SyntaxToken startDeclarationToken) => Update(startDeclarationToken, Attributes, EndDeclarationToken);
    public XmlDeclarationSyntax WithAttributes(SyntaxList<XmlAttributeSyntax> attributes) => Update(StartDeclarationToken, attributes, EndDeclarationToken);
    public XmlDeclarationSyntax WithEndDeclarationToken(SyntaxToken endDeclarationToken) => Update(StartDeclarationToken, Attributes, endDeclarationToken);

    internal override SyntaxNode? GetNodeSlot(int index) => index switch
{
        1 => GetRed(ref _attributes, 1),
        _ => null,
    };

    internal override SyntaxNode? GetCachedSlot(int index) => index switch
{
        1 => _attributes,
        _ => null,
    };

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitDeclaration(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitDeclaration(this);
    }
}
