using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
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

    /// <summary>Returns this inline link with the destination <paramref name="destination"/>, or itself when it has this destination.</summary>
    /// <param name="destination">The destination, which is escaped: it is the <see cref="Destination"/> of the link.</param>
    /// <remarks>
    /// The link is in no tree: replace this one with it, with <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.
    /// The destination of a reference link is the one of its definition: change the definition with
    /// <see cref="MarkdownLinkReferenceDefinitionSyntax.WithDestination(string)"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The link is a reference link.</exception>
    public MarkdownLinkSyntax WithDestination(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        EnsureInline();

        if (Destination == destination)
            return this;

        return WithDestination(this, destination, CloseIndex + 1);
    }

    /// <summary>Returns this inline link with the title <paramref name="title"/>, or itself when it has this title.</summary>
    /// <param name="title">The title, which is escaped: it is the <see cref="Title"/> of the link. <see langword="null"/> removes the title.</param>
    /// <remarks>
    /// The link is in no tree: replace this one with it, with <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.
    /// The title of a reference link is the one of its definition: change the definition with
    /// <see cref="MarkdownLinkReferenceDefinitionSyntax.WithTitle(string?)"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The link is a reference link.</exception>
    public MarkdownLinkSyntax WithTitle(string? title)
    {
        EnsureInline();

        if (Title == title)
            return this;

        return WithTitle(this, title, CloseIndex + 1);
    }

    private void EnsureInline()
    {
        if (LinkKind != MarkdownLinkKind.Inline)
            throw new InvalidOperationException("A reference link has the destination and the title of its definition: change the definition instead.");
    }

    // The destination and the title of a link or of a definition, whose tokens are after the child at start
    internal static TNode WithDestination<TNode>(TNode node, string destination, int start)
        where TNode : MarkdownSyntaxNode
    {
        var text = EscapePipes(MarkdownEscaper.EscapeLinkDestination(destination));
        var children = MarkdownNodeEditor.GetChildren(node);
        var index = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.LinkDestinationToken, start);
        if (index >= 0)
        {
            children[index] = MarkdownNodeEditor.WithText(children[index]!, text);
        }
        else
        {
            // An inline link without destination, such as [a]()
            var open = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.OpenParenthesisToken, start);
            children.Insert(open + 1, MarkdownNodeEditor.Token(SyntaxKind.LinkDestinationToken, text, leadingTrivia: null, trailingTrivia: null));
        }

        return MarkdownNodeEditor.WithChildren(node, children);
    }

    internal static TNode WithTitle<TNode>(TNode node, string? title, int start)
        where TNode : MarkdownSyntaxNode
    {
        // A title can span lines: its tokens are separated by line endings
        var children = MarkdownNodeEditor.GetChildren(node);
        var first = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.LinkTitleToken, start);
        var last = MarkdownNodeEditor.LastIndexOfToken(children, SyntaxKind.LinkTitleToken);
        if (title is null)
        {
            if (first < 0)
                return node;

            MarkdownNodeEditor.RemoveOnLine(children, first, last);

            // A backslash that ends the destination of a link would escape its closing parenthesis
            if (node is MarkdownLinkSyntax link && children[first - 1] is { RawKind: (int)SyntaxKind.LinkDestinationToken } destinationToken && link.DestinationToken.Text.EndsWith('\\', StringComparison.Ordinal))
            {
                children[first - 1] = MarkdownNodeEditor.WithText(destinationToken, EscapePipes(MarkdownEscaper.EscapeLinkDestination(link.Destination)));
            }

            return MarkdownNodeEditor.WithChildren(node, children);
        }

        var text = EscapePipes(MarkdownEscaper.EscapeLinkTitle(title));
        if (first >= 0)
        {
            var firstToken = (GreenToken)children[first]!;
            var lastToken = (GreenToken)children[last]!;
            children.RemoveRange(first + 1, last - first);
            children[first] = MarkdownNodeEditor.Token(SyntaxKind.LinkTitleToken, text, firstToken.LeadingTrivia, lastToken.TrailingTrivia);
            return MarkdownNodeEditor.WithChildren(node, children);
        }

        // The title follows the destination, which an inline link without destination gets
        var destination = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.LinkDestinationToken, start);
        if (destination < 0)
        {
            destination = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.OpenParenthesisToken, start) + 1;
            children.Insert(destination, MarkdownNodeEditor.Token(SyntaxKind.LinkDestinationToken, "<>", leadingTrivia: null, trailingTrivia: null));
        }

        MarkdownNodeEditor.InsertOnLineAfter(children, destination, MarkdownNodeEditor.Token(SyntaxKind.LinkTitleToken, text, leadingTrivia: null, trailingTrivia: null));
        return MarkdownNodeEditor.WithChildren(node, children);
    }

    // In a cell of a table, a pipe would end the cell. The node can be in no tree, as the result of another edit, so the pipes
    // are escaped wherever it is: \| is read as a pipe everywhere.
    private static string EscapePipes(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);

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
