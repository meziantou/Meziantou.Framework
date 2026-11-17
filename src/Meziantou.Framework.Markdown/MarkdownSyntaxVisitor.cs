namespace Meziantou.Framework.Markdown;

/// <summary>Visits a Markdown node, calling the method that matches its type.</summary>
public abstract class MarkdownSyntaxVisitor
{
    /// <summary>Visits <paramref name="node"/>, doing nothing when it is <see langword="null"/>.</summary>
    public virtual void Visit(MarkdownSyntaxNode? node) => node?.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual void DefaultVisit(MarkdownSyntaxNode node)
    {
    }

    /// <summary>Visits a <see cref="MarkdownDocumentSyntax"/>.</summary>
    public virtual void VisitMarkdownDocument(MarkdownDocumentSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownParagraphSyntax"/>.</summary>
    public virtual void VisitMarkdownParagraph(MarkdownParagraphSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownHeadingSyntax"/>.</summary>
    public virtual void VisitMarkdownHeading(MarkdownHeadingSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownBlockQuoteSyntax"/>.</summary>
    public virtual void VisitMarkdownBlockQuote(MarkdownBlockQuoteSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownListSyntax"/>.</summary>
    public virtual void VisitMarkdownList(MarkdownListSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownListItemSyntax"/>.</summary>
    public virtual void VisitMarkdownListItem(MarkdownListItemSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCodeBlockSyntax"/>.</summary>
    public virtual void VisitMarkdownCodeBlock(MarkdownCodeBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownHtmlBlockSyntax"/>.</summary>
    public virtual void VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownThematicBreakSyntax"/>.</summary>
    public virtual void VisitMarkdownThematicBreak(MarkdownThematicBreakSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownLinkReferenceDefinitionSyntax"/>.</summary>
    public virtual void VisitMarkdownLinkReferenceDefinition(MarkdownLinkReferenceDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableSyntax"/>.</summary>
    public virtual void VisitMarkdownTable(MarkdownTableSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableRowSyntax"/>.</summary>
    public virtual void VisitMarkdownTableRow(MarkdownTableRowSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableCellSyntax"/>.</summary>
    public virtual void VisitMarkdownTableCell(MarkdownTableCellSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownEmphasisSyntax"/>.</summary>
    public virtual void VisitMarkdownEmphasis(MarkdownEmphasisSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownStrikethroughSyntax"/>.</summary>
    public virtual void VisitMarkdownStrikethrough(MarkdownStrikethroughSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownEmphasisExtraSyntax"/>.</summary>
    public virtual void VisitMarkdownEmphasisExtra(MarkdownEmphasisExtraSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFrontMatterSyntax"/>.</summary>
    public virtual void VisitMarkdownFrontMatter(MarkdownFrontMatterSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownMathBlockSyntax"/>.</summary>
    public virtual void VisitMarkdownMathBlock(MarkdownMathBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownMathInlineSyntax"/>.</summary>
    public virtual void VisitMarkdownMathInline(MarkdownMathInlineSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAlertSyntax"/>.</summary>
    public virtual void VisitMarkdownAlert(MarkdownAlertSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCustomContainerSyntax"/>.</summary>
    public virtual void VisitMarkdownCustomContainer(MarkdownCustomContainerSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFooterSyntax"/>.</summary>
    public virtual void VisitMarkdownFooter(MarkdownFooterSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownGridTableSyntax"/>.</summary>
    public virtual void VisitMarkdownGridTable(MarkdownGridTableSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownJiraLinkSyntax"/>.</summary>
    public virtual void VisitMarkdownJiraLink(MarkdownJiraLinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFigureCaptionSyntax"/>.</summary>
    public virtual void VisitMarkdownFigureCaption(MarkdownFigureCaptionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFigureSyntax"/>.</summary>
    public virtual void VisitMarkdownFigure(MarkdownFigureSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionTermSyntax"/>.</summary>
    public virtual void VisitMarkdownDefinitionTerm(MarkdownDefinitionTermSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionItemSyntax"/>.</summary>
    public virtual void VisitMarkdownDefinitionItem(MarkdownDefinitionItemSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionListSyntax"/>.</summary>
    public virtual void VisitMarkdownDefinitionList(MarkdownDefinitionListSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAbbreviationDefinitionSyntax"/>.</summary>
    public virtual void VisitMarkdownAbbreviationDefinition(MarkdownAbbreviationDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAbbreviationSyntax"/>.</summary>
    public virtual void VisitMarkdownAbbreviation(MarkdownAbbreviationSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFootnoteDefinitionSyntax"/>.</summary>
    public virtual void VisitMarkdownFootnoteDefinition(MarkdownFootnoteDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFootnoteReferenceSyntax"/>.</summary>
    public virtual void VisitMarkdownFootnoteReference(MarkdownFootnoteReferenceSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownGenericAttributesSyntax"/>.</summary>
    public virtual void VisitMarkdownGenericAttributes(MarkdownGenericAttributesSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCodeSpanSyntax"/>.</summary>
    public virtual void VisitMarkdownCodeSpan(MarkdownCodeSpanSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownLinkSyntax"/>.</summary>
    public virtual void VisitMarkdownLink(MarkdownLinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAutolinkSyntax"/>.</summary>
    public virtual void VisitMarkdownAutolink(MarkdownAutolinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownRawHtmlSyntax"/>.</summary>
    public virtual void VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownExtensionInlineSyntax"/>.</summary>
    public virtual void VisitMarkdownExtensionInline(MarkdownExtensionInlineSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownExtensionBlockSyntax"/>.</summary>
    public virtual void VisitMarkdownExtensionBlock(MarkdownExtensionBlockSyntax node) => DefaultVisit(node);
}

/// <summary>Visits a Markdown node, calling the method that matches its type, and returns a result.</summary>
public abstract class MarkdownSyntaxVisitor<TResult>
{
    /// <summary>Visits <paramref name="node"/>, returning the default result when it is <see langword="null"/>.</summary>
    public virtual TResult? Visit(MarkdownSyntaxNode? node) => node is null ? default : node.Accept(this);

    /// <summary>Called for any node whose own method is not overridden.</summary>
    public virtual TResult? DefaultVisit(MarkdownSyntaxNode node) => default;

    /// <summary>Visits a <see cref="MarkdownDocumentSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownDocument(MarkdownDocumentSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownParagraphSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownParagraph(MarkdownParagraphSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownHeadingSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownHeading(MarkdownHeadingSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownBlockQuoteSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownBlockQuote(MarkdownBlockQuoteSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownListSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownList(MarkdownListSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownListItemSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownListItem(MarkdownListItemSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCodeBlockSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownCodeBlock(MarkdownCodeBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownHtmlBlockSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownThematicBreakSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownThematicBreak(MarkdownThematicBreakSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownLinkReferenceDefinitionSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownLinkReferenceDefinition(MarkdownLinkReferenceDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownTable(MarkdownTableSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableRowSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownTableRow(MarkdownTableRowSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownTableCellSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownTableCell(MarkdownTableCellSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownEmphasisSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownEmphasis(MarkdownEmphasisSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownStrikethroughSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownStrikethrough(MarkdownStrikethroughSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownEmphasisExtraSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownEmphasisExtra(MarkdownEmphasisExtraSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFrontMatterSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFrontMatter(MarkdownFrontMatterSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownMathBlockSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownMathBlock(MarkdownMathBlockSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownMathInlineSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownMathInline(MarkdownMathInlineSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAlertSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownAlert(MarkdownAlertSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCustomContainerSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownCustomContainer(MarkdownCustomContainerSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFooterSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFooter(MarkdownFooterSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownGridTableSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownGridTable(MarkdownGridTableSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownJiraLinkSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownJiraLink(MarkdownJiraLinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFigureCaptionSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFigureCaption(MarkdownFigureCaptionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFigureSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFigure(MarkdownFigureSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionTermSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownDefinitionTerm(MarkdownDefinitionTermSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionItemSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownDefinitionItem(MarkdownDefinitionItemSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownDefinitionListSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownDefinitionList(MarkdownDefinitionListSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAbbreviationDefinitionSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownAbbreviationDefinition(MarkdownAbbreviationDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAbbreviationSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownAbbreviation(MarkdownAbbreviationSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFootnoteDefinitionSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFootnoteDefinition(MarkdownFootnoteDefinitionSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownFootnoteReferenceSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownFootnoteReference(MarkdownFootnoteReferenceSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownGenericAttributesSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownGenericAttributes(MarkdownGenericAttributesSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownCodeSpanSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownCodeSpan(MarkdownCodeSpanSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownLinkSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownLink(MarkdownLinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownAutolinkSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownAutolink(MarkdownAutolinkSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownRawHtmlSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownExtensionInlineSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownExtensionInline(MarkdownExtensionInlineSyntax node) => DefaultVisit(node);

    /// <summary>Visits a <see cref="MarkdownExtensionBlockSyntax"/>.</summary>
    public virtual TResult? VisitMarkdownExtensionBlock(MarkdownExtensionBlockSyntax node) => DefaultVisit(node);
}
