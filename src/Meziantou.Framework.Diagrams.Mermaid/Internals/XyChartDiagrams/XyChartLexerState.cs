namespace Meziantou.Framework.Diagrams.Mermaid.Internals.XyChartDiagrams;

/// <summary>The states of the lexer of the XY charts, which change the rules that apply.</summary>
internal enum XyChartLexerState
{
    Initial,
    String,
    MarkdownString,
    AccessibleTitle,
    AccessibleDescription,
    AccessibleDescriptionBlock,
    AxisData,
    AxisBandData,
    Data,
    DataInner,
}
