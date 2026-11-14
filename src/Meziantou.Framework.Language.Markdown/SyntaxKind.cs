namespace Meziantou.Framework.Language.Markdown;

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
    MathDelimiterToken,
    MathTextToken,
    ExtendedAutolinkToken,

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
    TableHeaderRow,
    TableDelimiterRow,
    TableRow,
    TableCell,
    FrontMatter,
    MathBlock,

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
}
