namespace Meziantou.Framework.Pdf.Parsing;

internal enum PdfTokenKind
{
    EndOfInput,
    Integer,
    Real,
    LiteralString,
    HexString,
    Name,

    /// <summary>A sequence of regular characters that is not a number: <c>true</c>, <c>obj</c>, <c>R</c>, an operator of a content stream.</summary>
    Keyword,
    ArrayStart,
    ArrayEnd,
    DictionaryStart,
    DictionaryEnd,
    ProcedureStart,
    ProcedureEnd,

    /// <summary>A delimiter that cannot start a token (<c>)</c> or <c>&gt;</c> alone).</summary>
    Invalid,
}
