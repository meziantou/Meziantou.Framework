namespace Meziantou.Framework.Fonts;

/// <summary>The format of the outlines of a font.</summary>
internal enum FontOutlineKind
{
    /// <summary>The font has no outline table: its glyphs are bitmaps, or it only carries metrics.</summary>
    None = 0,

    /// <summary>Quadratic outlines in the <c>glyf</c> table.</summary>
    TrueType = 1,

    /// <summary>Cubic outlines in the <c>CFF </c> table.</summary>
    Cff = 2,

    /// <summary>Cubic outlines in the <c>CFF2</c> table.</summary>
    Cff2 = 3,
}
