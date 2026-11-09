namespace Meziantou.Framework.Diagrams.Mermaid.Internals.VennDiagrams;

/// <summary>The tokens of the lexer of the Venn diagrams of Mermaid.</summary>
internal enum VennTokenKind
{
    Title,
    Venn,
    Set,
    Union,
    Text,
    IndentText,
    Style,
    BracketLabel,
    Numeric,
    HexColor,
    RgbaColor,
    RgbColor,
    Identifier,
    String,
    Comma,
    Colon,
    Newline,
    EndOfFile,
}
