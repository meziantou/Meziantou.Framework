namespace Meziantou.Framework.Fonts;

/// <summary>The kinds of the tokens of the PostScript language that a Type 1 font program uses.</summary>
internal enum PostScriptTokenKind
{
    EndOfData,
    Number,
    LiteralName,
    ExecutableName,
    String,
    HexString,
    ArrayStart,
    ArrayEnd,
    ProcedureStart,
    ProcedureEnd,
    DictionaryStart,
    DictionaryEnd,
}
