using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown.Syntax.InternalSyntax;

/// <summary>
/// The green node of every Markdown construct: a kind and its children, tokens or nodes, in the order of the text. A typed
/// API reads the children by kind, so a new construct only needs a new kind.
/// </summary>
/// <remarks>
/// The children are in one list slot: the red tree finds a child of a list in constant time, so walking the thousands of
/// blocks of a document is linear.
/// </remarks>
internal sealed class MarkdownNode : GreenNode
{
    private readonly GreenNode? _children;

    public MarkdownNode(SyntaxKind kind, ReadOnlySpan<GreenNode?> children)
        : this(kind, Green.SyntaxList.ListNode(children), diagnostics: null, annotations: null, GetRareKinds(children))
    {
    }

    private MarkdownNode(SyntaxKind kind, GreenNode? children, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations, MarkdownRareKinds rareKinds)
        : base((int)kind, diagnostics, annotations)
    {
        _children = children;
        SlotCount = 1;
        AdjustFlagsAndWidth(children);
        RareKinds = rareKinds;
    }

    /// <summary>Gets the kinds of the nodes below this one that most documents have none of.</summary>
    /// <remarks>They are known when the node is made, so that a document does not have to be walked to find them.</remarks>
    public MarkdownRareKinds RareKinds { get; }

    // The children are read from the span the parser gives, rather than from the list, whose slots are virtual calls
    private static MarkdownRareKinds GetRareKinds(ReadOnlySpan<GreenNode?> children)
    {
        var kinds = MarkdownRareKinds.None;
        foreach (var child in children)
        {
            kinds |= GetRareKinds(child);
        }

        return kinds;
    }

    private static MarkdownRareKinds GetRareKinds(GreenNode? node)
    {
        switch (node)
        {
            case MarkdownNode markdownNode:
                return markdownNode.RareKinds | (SyntaxKind)node.RawKind switch
                {
                    SyntaxKind.GenericAttributes => MarkdownRareKinds.GenericAttributes,
                    SyntaxKind.GridTable => MarkdownRareKinds.GridTable,
                    _ => MarkdownRareKinds.None,
                };

            case { IsList: true }:
                var kinds = MarkdownRareKinds.None;
                for (var i = 0; i < node.SlotCount; i++)
                {
                    kinds |= GetRareKinds(node.GetSlot(i));
                }

                return kinds;

            default:
                return MarkdownRareKinds.None;
        }
    }

    public override string KindText => ((SyntaxKind)RawKind).ToString();

    internal override GreenNode? GetSlot(int index) => index == 0 ? _children : null;

    internal override bool IsListSlot(int index) => index == 0;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots)
    {
        // The slot is the list of the children, or a single child that replaced it
        var children = slots[0] switch
        {
            null => null,
            { IsList: true } list => list,
            var child => Green.SyntaxList.ListNode([child]),
        };

        return new MarkdownNode((SyntaxKind)RawKind, children, GetDiagnostics(), GetAnnotations(), GetRareKinds(children));
    }

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new MarkdownNode((SyntaxKind)RawKind, _children, diagnostics, GetAnnotations(), RareKinds);

    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new MarkdownNode((SyntaxKind)RawKind, _children, GetDiagnostics(), annotations, RareKinds);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;

    // Each kind has a type of red node, which reads the children of its construct
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => (SyntaxKind)RawKind switch
        {
            SyntaxKind.Document => new Markdown.MarkdownDocumentSyntax(this, parent, position),
            SyntaxKind.Paragraph => new Markdown.MarkdownParagraphSyntax(this, parent, position),
            SyntaxKind.AtxHeading or SyntaxKind.SetextHeading => new Markdown.MarkdownHeadingSyntax(this, parent, position),
            SyntaxKind.BlockQuote => new Markdown.MarkdownBlockQuoteSyntax(this, parent, position),
            SyntaxKind.List => new Markdown.MarkdownListSyntax(this, parent, position),
            SyntaxKind.ListItem => new Markdown.MarkdownListItemSyntax(this, parent, position),
            SyntaxKind.FencedCodeBlock or SyntaxKind.IndentedCodeBlock => new Markdown.MarkdownCodeBlockSyntax(this, parent, position),
            SyntaxKind.HtmlBlock => new Markdown.MarkdownHtmlBlockSyntax(this, parent, position),
            SyntaxKind.ThematicBreak => new Markdown.MarkdownThematicBreakSyntax(this, parent, position),
            SyntaxKind.LinkReferenceDefinition => new Markdown.MarkdownLinkReferenceDefinitionSyntax(this, parent, position),
            SyntaxKind.Table or SyntaxKind.PipeTable => new Markdown.MarkdownTableSyntax(this, parent, position),
            SyntaxKind.GridTable => new Markdown.MarkdownGridTableSyntax(this, parent, position),
            SyntaxKind.JiraLink => new Markdown.MarkdownJiraLinkSyntax(this, parent, position),
            SyntaxKind.TableHeaderRow or SyntaxKind.TableDelimiterRow or SyntaxKind.TableRow => new Markdown.MarkdownTableRowSyntax(this, parent, position),
            SyntaxKind.TableCell => new Markdown.MarkdownTableCellSyntax(this, parent, position),
            SyntaxKind.Emphasis or SyntaxKind.StrongEmphasis => new Markdown.MarkdownEmphasisSyntax(this, parent, position),
            SyntaxKind.Strikethrough => new Markdown.MarkdownStrikethroughSyntax(this, parent, position),
            SyntaxKind.FrontMatter => new Markdown.MarkdownFrontMatterSyntax(this, parent, position),
            SyntaxKind.MathBlock => new Markdown.MarkdownMathBlockSyntax(this, parent, position),
            SyntaxKind.Alert => new Markdown.MarkdownAlertSyntax(this, parent, position),
            SyntaxKind.CustomContainer => new Markdown.MarkdownCustomContainerSyntax(this, parent, position),
            SyntaxKind.DefinitionList => new Markdown.MarkdownDefinitionListSyntax(this, parent, position),
            SyntaxKind.DefinitionItem => new Markdown.MarkdownDefinitionItemSyntax(this, parent, position),
            SyntaxKind.DefinitionTerm => new Markdown.MarkdownDefinitionTermSyntax(this, parent, position),
            SyntaxKind.Figure => new Markdown.MarkdownFigureSyntax(this, parent, position),
            SyntaxKind.FigureCaption => new Markdown.MarkdownFigureCaptionSyntax(this, parent, position),
            SyntaxKind.Footer => new Markdown.MarkdownFooterSyntax(this, parent, position),
            SyntaxKind.AbbreviationDefinition => new Markdown.MarkdownAbbreviationDefinitionSyntax(this, parent, position),
            SyntaxKind.Abbreviation => new Markdown.MarkdownAbbreviationSyntax(this, parent, position),
            SyntaxKind.FootnoteDefinition => new Markdown.MarkdownFootnoteDefinitionSyntax(this, parent, position),
            SyntaxKind.FootnoteReference => new Markdown.MarkdownFootnoteReferenceSyntax(this, parent, position),
            SyntaxKind.GenericAttributes => new Markdown.MarkdownGenericAttributesSyntax(this, parent, position),
            SyntaxKind.MathInline => new Markdown.MarkdownMathInlineSyntax(this, parent, position),
            SyntaxKind.ExtensionInline => new Markdown.MarkdownExtensionInlineSyntax(this, parent, position),
            SyntaxKind.ExtensionBlock => new Markdown.MarkdownExtensionBlockSyntax(this, parent, position),
            SyntaxKind.Subscript or SyntaxKind.Superscript or SyntaxKind.Inserted or SyntaxKind.Marked or SyntaxKind.CustomContainerInline or SyntaxKind.Citation => new Markdown.MarkdownEmphasisExtraSyntax(this, parent, position),
            SyntaxKind.CodeSpan => new Markdown.MarkdownCodeSpanSyntax(this, parent, position),
            SyntaxKind.Link or SyntaxKind.Image => new Markdown.MarkdownLinkSyntax(this, parent, position),
            SyntaxKind.Autolink or SyntaxKind.ExtendedAutolink or SyntaxKind.MarkdigAutolink => new Markdown.MarkdownAutolinkSyntax(this, parent, position),
            SyntaxKind.RawHtml => new Markdown.MarkdownRawHtmlSyntax(this, parent, position),
            _ => throw new InvalidOperationException($"The kind {(SyntaxKind)RawKind} is not a node."),
        };
}
