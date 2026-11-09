namespace Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>A token of a quadrant chart: its kind, its range in the text, its value, and the coordinates of a point.</summary>
internal readonly record struct QuadrantChartToken(QuadrantChartTokenKind Kind, int Start, int End, string Value, double X = 0, double Y = 0)
{
    /// <summary>Gets a value that indicates whether the token can start a text (the <c>alphaNumToken</c> of the grammar of Mermaid).</summary>
    public bool IsAlphaNumeric => Kind is QuadrantChartTokenKind.Punctuation or QuadrantChartTokenKind.Amp or QuadrantChartTokenKind.Num or QuadrantChartTokenKind.Alpha
        or QuadrantChartTokenKind.Comma or QuadrantChartTokenKind.Plus or QuadrantChartTokenKind.Equals or QuadrantChartTokenKind.Mult or QuadrantChartTokenKind.Dot
        or QuadrantChartTokenKind.Hash or QuadrantChartTokenKind.Underscore or QuadrantChartTokenKind.Unicode;

    /// <summary>Gets a value that indicates whether the token can continue a text.</summary>
    public bool IsText => IsAlphaNumeric || Kind is QuadrantChartTokenKind.Space or QuadrantChartTokenKind.Minus;

    /// <summary>Gets a value that indicates whether the token can be part of the name of a class (the <c>idString</c> of the grammar of Mermaid).</summary>
    public bool IsClassNamePart => Kind is QuadrantChartTokenKind.Alpha or QuadrantChartTokenKind.Num or QuadrantChartTokenKind.Minus or QuadrantChartTokenKind.Comma
        or QuadrantChartTokenKind.Colon or QuadrantChartTokenKind.Amp or QuadrantChartTokenKind.Hash or QuadrantChartTokenKind.Mult or QuadrantChartTokenKind.Unicode;

    /// <summary>Gets a value that indicates whether the token can be part of a style (the <c>styleComponent</c> of the grammar of Mermaid).</summary>
    public bool IsStylePart => Kind is QuadrantChartTokenKind.Alpha or QuadrantChartTokenKind.Num or QuadrantChartTokenKind.Colon or QuadrantChartTokenKind.Space
        or QuadrantChartTokenKind.Hash or QuadrantChartTokenKind.Minus;

    /// <summary>Gets a value that indicates whether the token ends a statement.</summary>
    public bool IsStatementEnd => Kind is QuadrantChartTokenKind.NewLine or QuadrantChartTokenKind.Semicolon or QuadrantChartTokenKind.End;
}
