namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What a <see cref="FilterInput"/> is.</summary>
public enum FilterInputKind
{
    /// <summary>The result of the primitive before, or the drawing for the first primitive: what SVG takes when <c>in</c> is not given.</summary>
    Previous = 0,

    /// <summary>The drawing that is filtered: <c>SourceGraphic</c>.</summary>
    SourceGraphic = 1,

    /// <summary>The alpha of the drawing, black: <c>SourceAlpha</c>.</summary>
    SourceAlpha = 2,

    /// <summary>The result of an earlier primitive of the graph, by its index: a named <c>result</c>.</summary>
    Result = 3,
}
