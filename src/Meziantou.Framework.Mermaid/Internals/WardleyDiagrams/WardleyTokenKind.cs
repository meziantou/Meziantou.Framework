namespace Meziantou.Framework.Mermaid.Internals.WardleyDiagrams;

/// <summary>The tokens of the Langium grammar of the Wardley maps of Mermaid.</summary>
internal enum WardleyTokenKind
{
    End,
    Unknown,
    Newline,
    LeftBracket,
    Comma,
    RightBracket,
    Minus,
    LeftParenthesis,
    RightParenthesis,
    LeftBrace,
    RightBrace,
    At,
    Slash,

    /// <summary>A decimal number, such as <c>0.5</c> (<c>WARDLEY_NUMBER</c>).</summary>
    Number,
    Arrow,
    LinkPort,
    LinkArrow,
    LinkLabel,
    Strategy,
    KeywordWardley,
    KeywordSize,
    KeywordEvolution,
    KeywordAnchor,
    KeywordComponent,
    KeywordLabel,
    KeywordInertia,
    KeywordEvolve,
    KeywordPipeline,
    KeywordNote,
    KeywordAnnotations,
    KeywordAnnotation,
    KeywordAccelerator,
    KeywordDeaccelerator,

    /// <summary>A name with spaces, such as <c>Cup of Tea</c> (<c>NAME_WITH_SPACES</c>).</summary>
    Name,
    AccessibleDescription,
    AccessibleTitle,
    Title,
    Integer,
    String,
    Identifier,
}
