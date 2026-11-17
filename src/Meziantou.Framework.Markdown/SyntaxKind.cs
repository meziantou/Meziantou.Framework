namespace Meziantou.Framework.Markdown;

/// <summary>The kinds of the nodes, tokens and trivia of a Markdown syntax tree.</summary>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A list of nodes or trivia, a kind that every language of the framework reserves.</summary>
    SyntaxList = 1,

    // Trivia: the characters that no token covers
    WhitespaceTrivia,
    EndOfLineTrivia,
    BlockQuoteMarkerTrivia,
    ListItemIndentTrivia,
    FooterMarkerTrivia,
    SkippedTextTrivia,

    // Tokens of the blocks
    EndOfFileToken,
    BlockQuoteMarkerToken,
    ListMarkerToken,
    ThematicBreakToken,
    AtxHeadingMarkerToken,
    AtxHeadingClosingSequenceToken,
    SetextHeadingUnderlineToken,
    CodeFenceToken,
    InfoStringToken,
    CodeLineToken,
    HtmlLineToken,

    // Tokens of the inlines
    TextToken,
    EscapedCharacterToken,
    EntityToken,
    LineEndingToken,
    HardLineBreakToken,
    EmphasisDelimiterToken,
    BacktickStringToken,
    CodeTextToken,
    OpenBracketToken,
    ExclamationOpenBracketToken,
    CloseBracketToken,
    OpenParenthesisToken,
    CloseParenthesisToken,
    LinkDestinationToken,
    LinkTitleToken,
    LinkLabelToken,
    ColonToken,
    AutolinkToken,
    HtmlTagToken,

    // Tokens of the extensions
    StrikethroughDelimiterToken,
    PipeToken,
    TableDelimiterToken,
    TaskListItemMarkerToken,
    FrontMatterFenceToken,
    FrontMatterLineToken,
    MathFenceToken,
    MathLineToken,
    GridTableLineToken,
    MathDelimiterToken,
    MathTextToken,
    AlertKindToken,
    AsteriskToken,
    AbbreviationLabelToken,
    AbbreviationTextToken,
    FootnoteLabelToken,
    AttributesToken,
    AttributesTrailingTextToken,
    ExtendedAutolinkToken,
    CustomContainerFenceToken,
    CustomContainerArgumentsToken,
    DefinitionMarkerToken,
    SmartyPantToken,
    EmojiToken,
    JiraLinkToken,
    FigureFenceToken,
    FooterMarkerToken,

    /// <summary>A marker of a <see cref="MarkdownExtensionInlineSyntax"/> or of a <see cref="MarkdownExtensionBlockSyntax"/>, such as the <c>@</c> of a mention or a run of a delimiter extension.</summary>
    ExtensionMarkerToken,

    /// <summary>Text of a <see cref="MarkdownExtensionInlineSyntax"/> or of a <see cref="MarkdownExtensionBlockSyntax"/>.</summary>
    ExtensionTextToken,

    /// <summary>A fence of a <see cref="MarkdownExtensionBlockSyntax"/> of a <see cref="MarkdownFencedBlockExtension"/>.</summary>
    ExtensionFenceToken,

    /// <summary>A line between the fences of a <see cref="MarkdownExtensionBlockSyntax"/>, with its line ending.</summary>
    ExtensionLineToken,

    // Blocks
    Document,
    BlockQuote,
    List,
    ListItem,
    ThematicBreak,
    AtxHeading,
    SetextHeading,
    FencedCodeBlock,
    IndentedCodeBlock,
    HtmlBlock,
    Paragraph,
    LinkReferenceDefinition,
    Table,
    PipeTable,
    GridTable,
    TableHeaderRow,
    TableDelimiterRow,
    TableRow,
    TableCell,
    FrontMatter,
    MathBlock,
    Alert,
    AbbreviationDefinition,
    FootnoteDefinition,
    CustomContainer,
    DefinitionList,
    DefinitionItem,
    DefinitionTerm,
    Figure,
    FigureCaption,
    Footer,

    /// <summary>A block of a <see cref="MarkdownFencedBlockExtension"/> or of a <see cref="MarkdownParagraphExtension"/>.</summary>
    ExtensionBlock,

    // Inlines
    Emphasis,
    StrongEmphasis,
    CodeSpan,
    Link,
    Image,
    Autolink,
    RawHtml,
    Strikethrough,
    ExtendedAutolink,
    Subscript,
    Superscript,
    Inserted,
    Marked,
    MathInline,
    Abbreviation,
    FootnoteReference,
    GenericAttributes,
    CustomContainerInline,
    Citation,
    JiraLink,
    MarkdigAutolink,

    /// <summary>A construct of a <see cref="MarkdownInlineExtension"/> or of a <see cref="MarkdownDelimiterExtension"/>.</summary>
    ExtensionInline,
}
