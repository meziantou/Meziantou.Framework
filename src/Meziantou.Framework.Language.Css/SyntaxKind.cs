namespace Meziantou.Framework.Language.Css;

/// <summary>The kinds of node, token, and trivia a CSS tree is made of.</summary>
/// <remarks>
/// The tokens are those of the CSS tokenizer. Whitespace and comments are trivia rather than tokens, and the delimiters
/// the grammars give a meaning to have a kind of their own; any other delimiter is a <see cref="DelimToken"/>.
/// A keyword such as <c>and</c> or <c>important</c> stays an <see cref="IdentToken"/>, since CSS compares keywords
/// without regard to case and a node already says what role each of its tokens plays.
/// </remarks>
public enum SyntaxKind
{
    None = 0,

    /// <summary>A sequence of children held in one slot. Shared by every language, and so fixed at 1.</summary>
    List = 1,

    /// <summary>An identifier, such as <c>color</c> or <c>--main-color</c>.</summary>
    IdentToken = 8000,

    /// <summary>An identifier followed by <c>(</c>, such as <c>rgb(</c>. Its value is the name without the parenthesis.</summary>
    FunctionToken = 8001,

    /// <summary><c>@</c> followed by an identifier, such as <c>@media</c>. Its value is the name without the <c>@</c>.</summary>
    AtKeywordToken = 8002,

    /// <summary><c>#</c> followed by a name, such as <c>#main</c> or <c>#fff</c>. Its value is the name without the <c>#</c>.</summary>
    HashToken = 8003,

    /// <summary>A quoted string. Its value is the text between the quotes, with the escapes resolved.</summary>
    StringToken = 8004,

    /// <summary>A string that a line break ended before its closing quote.</summary>
    BadStringToken = 8005,

    /// <summary>An unquoted <c>url(...)</c>. Its value is the address, with the escapes resolved.</summary>
    UrlToken = 8006,

    /// <summary>An unquoted <c>url(...)</c> holding a character it cannot hold, such as a quote or whitespace in the middle.</summary>
    BadUrlToken = 8007,

    /// <summary>A number, such as <c>12</c> or <c>-0.5e3</c>.</summary>
    NumberToken = 8008,

    /// <summary>A number followed by <c>%</c>.</summary>
    PercentageToken = 8009,

    /// <summary>A number followed by a unit, such as <c>10px</c>.</summary>
    DimensionToken = 8010,

    /// <summary><c>&lt;!--</c>, which the top level of a style sheet ignores.</summary>
    CdoToken = 8011,

    /// <summary><c>--&gt;</c>, which the top level of a style sheet ignores.</summary>
    CdcToken = 8012,

    /// <summary>A <c>U+0-7F</c> range in the value of a <c>unicode-range</c> descriptor.</summary>
    UnicodeRangeToken = 8013,

    ColonToken = 8100,
    SemicolonToken = 8101,
    CommaToken = 8102,
    OpenBracketToken = 8103,
    CloseBracketToken = 8104,
    OpenParenToken = 8105,
    CloseParenToken = 8106,
    OpenBraceToken = 8107,
    CloseBraceToken = 8108,

    /// <summary>A single character that is not part of any other token, and has no kind of its own.</summary>
    DelimToken = 8200,

    /// <summary><c>.</c>, which starts a class selector.</summary>
    DotToken = 8201,

    /// <summary><c>*</c>, the universal selector.</summary>
    AsteriskToken = 8202,

    /// <summary><c>&amp;</c>, the nesting selector.</summary>
    AmpersandToken = 8203,

    GreaterThanToken = 8204,
    LessThanToken = 8205,
    EqualsToken = 8206,
    PlusToken = 8207,
    MinusToken = 8208,
    TildeToken = 8209,

    /// <summary><c>|</c>, which separates a namespace prefix from a name.</summary>
    BarToken = 8210,

    CaretToken = 8211,
    DollarToken = 8212,

    /// <summary><c>!</c>, which starts <c>!important</c>.</summary>
    ExclamationToken = 8213,

    SlashToken = 8214,

    /// <summary>The end of the text. It holds the trivia after the last token.</summary>
    EndOfFileToken = 8400,

    /// <summary>Spaces and tabs.</summary>
    WhitespaceTrivia = 8500,

    /// <summary>A line break: <c>\n</c>, <c>\r\n</c>, <c>\r</c>, or a form feed.</summary>
    EndOfLineTrivia = 8501,

    /// <summary>A <c>/* ... */</c> comment.</summary>
    MultiLineCommentTrivia = 8502,

    /// <summary>A whole style sheet.</summary>
    StyleSheet = 9000,

    /// <summary>A style rule, such as <c>a { color: red }</c>, whose prelude is a selector list.</summary>
    StyleRule = 9001,

    /// <summary>A rule in a <c>@keyframes</c> block, such as <c>from { opacity: 0 }</c>.</summary>
    KeyframeRule = 9002,

    /// <summary>A qualified rule that is neither a style rule nor a keyframe rule, such as one in an unknown at-rule.</summary>
    QualifiedRule = 9003,

    CharsetRule = 9010,
    ImportRule = 9011,
    NamespaceRule = 9012,
    MediaRule = 9013,
    SupportsRule = 9014,
    ContainerRule = 9015,
    LayerRule = 9016,
    ScopeRule = 9017,
    StartingStyleRule = 9018,
    KeyframesRule = 9019,
    FontFaceRule = 9020,
    PageRule = 9021,

    /// <summary>A margin rule of a <c>@page</c> rule, such as <c>@top-left</c>.</summary>
    PageMarginRule = 9022,

    PropertyRule = 9023,
    CounterStyleRule = 9024,
    FontFeatureValuesRule = 9025,

    /// <summary>A feature block of a <c>@font-feature-values</c> rule, such as <c>@swash</c> or <c>@styleset</c>.</summary>
    FontFeatureValueBlockRule = 9026,

    FontPaletteValuesRule = 9027,
    PositionTryRule = 9028,
    ViewTransitionRule = 9029,
    CustomMediaRule = 9030,
    FunctionRule = 9031,

    /// <summary>An at-rule this parser does not know, or a known one where it does not belong.</summary>
    UnknownAtRule = 9049,

    /// <summary>The <c>{ ... }</c> body of a rule.</summary>
    Block = 9050,

    Declaration = 9051,

    /// <summary>The <c>!important</c> at the end of a declaration.</summary>
    Important = 9052,

    /// <summary>Text in a block that is neither a declaration nor a rule, which a browser drops.</summary>
    BadDeclaration = 9053,

    /// <summary>A token a style sheet or a block ignores, such as a stray <c>;</c>.</summary>
    IgnoredToken = 9054,

    /// <summary>Text the parser could not use, such as a block nested beyond the depth limit.</summary>
    SkippedText = 9055,

    /// <summary>A component value that is a single token.</summary>
    TokenValue = 9100,

    Function = 9101,

    /// <summary>A <c>( ... )</c> block.</summary>
    ParenthesizedBlock = 9102,

    /// <summary>A <c>[ ... ]</c> block.</summary>
    BracketedBlock = 9103,

    /// <summary>A <c>{ ... }</c> block in a value.</summary>
    BracedBlock = 9104,

    /// <summary>Values nested beyond the depth limit.</summary>
    SkippedValue = 9105,

    /// <summary>A prelude kept as component values, either because the rule has no grammar here or because it does not match it.</summary>
    GenericPrelude = 9106,

    SelectorList = 9200,
    ComplexSelector = 9201,
    ComplexSelectorPart = 9202,

    /// <summary><c>&gt;</c></summary>
    ChildCombinator = 9203,

    /// <summary><c>+</c></summary>
    NextSiblingCombinator = 9204,

    /// <summary><c>~</c></summary>
    SubsequentSiblingCombinator = 9205,

    /// <summary><c>||</c></summary>
    ColumnCombinator = 9206,

    CompoundSelector = 9207,

    /// <summary>An item of a forgiving selector list, such as the argument of <c>:is()</c>, that does not parse and so matches nothing.</summary>
    InvalidSelector = 9208,

    NamespacePrefix = 9209,
    TypeSelector = 9210,
    UniversalSelector = 9211,
    NestingSelector = 9212,
    IdSelector = 9213,
    ClassSelector = 9214,
    AttributeSelector = 9215,
    PseudoClassSelector = 9216,
    PseudoElementSelector = 9217,
    FunctionalPseudoClassSelector = 9218,
    FunctionalPseudoElementSelector = 9219,
    NthArgument = 9220,
    AnPlusB = 9221,
    NameList = 9222,
    ViewTransitionPartSelector = 9223,

    NamePrelude = 9300,
    ImportPrelude = 9301,
    ImportLayer = 9302,
    ImportSupports = 9303,
    NamespacePrelude = 9304,
    LayerName = 9305,
    LayerPrelude = 9306,
    ScopePrelude = 9307,
    ScopeBoundary = 9308,
    KeyframeSelectorList = 9309,
    KeyframeSelector = 9310,
    PageSelectorList = 9311,
    PageSelector = 9312,
    PseudoPage = 9313,
    CustomMediaPrelude = 9314,
    ContainerPrelude = 9315,
    ContainerCondition = 9316,
    ConditionPrelude = 9317,

    MediaQueryList = 9400,
    MediaTypeQuery = 9401,
    MediaConditionQuery = 9402,

    /// <summary>A media query that does not parse, which matches nothing without invalidating the rest of the list.</summary>
    InvalidMediaQuery = 9403,

    NotCondition = 9410,
    AndCondition = 9411,
    OrCondition = 9412,
    ParenthesizedCondition = 9413,

    /// <summary>A parenthesized or functional condition this parser has no grammar for. It is valid and evaluates to false.</summary>
    GeneralEnclosed = 9414,

    /// <summary>A feature with a value, such as <c>(min-width: 600px)</c>.</summary>
    PlainFeature = 9415,

    /// <summary>A feature without a value, such as <c>(hover)</c>.</summary>
    BooleanFeature = 9416,

    /// <summary>A feature compared to values, such as <c>(400px &lt;= width &lt; 800px)</c>.</summary>
    RangeFeature = 9417,

    LessThanComparison = 9418,
    LessThanOrEqualComparison = 9419,
    GreaterThanComparison = 9420,
    GreaterThanOrEqualComparison = 9421,
    EqualComparison = 9422,
    FeatureValue = 9423,

    /// <summary>A declaration used as a condition, such as <c>(display: grid)</c> in <c>@supports</c>.</summary>
    DeclarationCondition = 9424,

    /// <summary><c>selector()</c> in <c>@supports</c>.</summary>
    SelectorFunction = 9425,

    /// <summary><c>font-tech()</c> in <c>@supports</c>.</summary>
    FontTechFunction = 9426,

    /// <summary><c>font-format()</c> in <c>@supports</c>.</summary>
    FontFormatFunction = 9427,

    /// <summary><c>at-rule()</c> in <c>@supports</c>.</summary>
    AtRuleFunction = 9428,

    /// <summary><c>style()</c> in <c>@container</c>.</summary>
    StyleQuery = 9429,

    /// <summary><c>scroll-state()</c> in <c>@container</c>.</summary>
    ScrollStateQuery = 9430,
}
