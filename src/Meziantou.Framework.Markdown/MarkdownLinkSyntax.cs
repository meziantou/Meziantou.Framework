using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A link (<c>[text](destination)</c>, <c>[text][label]</c>...) or an image (<c>![description](source)</c>).</summary>
public sealed class MarkdownLinkSyntax : MarkdownInlineSyntax
{
    internal MarkdownLinkSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether this is an image: its text is the description of the image.</summary>
    public bool IsImage => Kind() == SyntaxKind.Image;

    /// <summary>Gets how the link gives its destination.</summary>
    public MarkdownLinkKind LinkKind
    {
        get
        {
            var close = CloseIndex;
            var children = ChildNodesAndTokens();
            if (close + 1 >= children.Count)
            {
                return MarkdownLinkKind.Shortcut;
            }

            return (SyntaxKind)children[close + 1].AsToken().RawKind switch
            {
                SyntaxKind.OpenParenthesisToken => MarkdownLinkKind.Inline,
                _ when GetToken(SyntaxKind.LinkLabelToken).RawKind != 0 => MarkdownLinkKind.Full,
                _ => MarkdownLinkKind.Collapsed,
            };
        }
    }

    /// <summary>Gets the inlines of the text of the link, between its brackets.</summary>
    public IEnumerable<SyntaxNodeOrToken> Text => ChildNodesAndTokens().Skip(1).Take(CloseIndex - 1);

    /// <summary>Gets the label that refers to a link reference definition, or <see langword="null"/> for an inline link.</summary>
    /// <remarks>The label of a collapsed or a shortcut link is its text.</remarks>
    public string? Label => LinkKind switch
    {
        MarkdownLinkKind.Inline => null,
        MarkdownLinkKind.Full => GetTokensText(SyntaxKind.LinkLabelToken),
        _ => string.Concat(Text.SelectMany(child => child.IsToken ? [child.AsToken()] : child.AsNode()!.DescendantTokens()).Select(GetTokenText)),
    };

    /// <summary>Gets the definition that a reference link refers to, or <see langword="null"/>.</summary>
    public MarkdownLinkReferenceDefinitionSyntax? Definition => Label is { } label ? FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetLinkReferenceDefinition(label) : null;

    /// <summary>Gets the heading that a reference link without definition links to with <see cref="MarkdownExtensions.AutoIdentifiers"/>, or <see langword="null"/>.</summary>
    public MarkdownHeadingSyntax? Heading => Label is { } label && Definition is null ? FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetHeading(label) : null;

    /// <summary>Gets the destination, without its angle brackets, escapes and entities: the one of the definition for a reference link.</summary>
    /// <remarks>
    /// A reference link to a <see cref="Heading"/> has <c>#</c> and the identifier of the heading as destination. It is empty when a
    /// reference link has no definition, which an edit can cause.
    /// </remarks>
    public string Destination => LinkKind == MarkdownLinkKind.Inline
        ? ReadDestination(this, DestinationToken)
        : Definition?.Destination ?? (Heading is { } heading ? MarkdownText.Unescape("#" + heading.Identifier) : "");

    /// <summary>Gets the title, without its quotes, escapes and entities, or <see langword="null"/> when there is none.</summary>
    public string? Title => LinkKind == MarkdownLinkKind.Inline ? ReadTitle(GetTokensText(SyntaxKind.LinkTitleToken)) : Definition?.Title;

    /// <summary>Gets the destination of an inline link, or the default token.</summary>
    public SyntaxToken DestinationToken => GetToken(SyntaxKind.LinkDestinationToken);

    // The index of the bracket that ends the text: the first closing bracket that is a child of the link
    internal int CloseIndex
    {
        get
        {
            var index = 0;
            foreach (var child in ChildNodesAndTokens())
            {
                if (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.CloseBracketToken)
                {
                    return index;
                }

                index++;
            }

            return index;
        }
    }

    internal override void AppendPlainText(StringBuilder builder) => AppendPlainText(Text, builder);

    internal static string ReadDestination(MarkdownSyntaxNode node, SyntaxToken token)
    {
        var destination = token.RawKind == 0 ? "" : node.GetTokenText(token);
        if (destination.StartsWith('<', StringComparison.Ordinal) && destination.EndsWith('>', StringComparison.Ordinal))
        {
            destination = destination[1..^1];
        }

        return MarkdownText.Unescape(destination);
    }

    internal static string? ReadTitle(string title) => title.Length < 2 ? null : MarkdownText.Unescape(title[1..^1]);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownLink(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownLink(this);
    }
}
