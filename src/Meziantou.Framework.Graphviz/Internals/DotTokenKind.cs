namespace Meziantou.Framework.Graphviz.Internals;

internal enum DotTokenKind
{
    End,
    Identifier,
    Numeral,
    QuotedString,
    HtmlString,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Equal,
    Semicolon,
    Comma,
    Colon,
    Plus,
    DirectedEdge,
    UndirectedEdge,
}
