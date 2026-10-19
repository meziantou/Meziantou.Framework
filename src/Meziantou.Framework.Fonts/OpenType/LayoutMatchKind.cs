namespace Meziantou.Framework.Fonts;

/// <summary>How the glyphs of a sequence of a contextual lookup are described: the three formats of the lookups.</summary>
internal enum LayoutMatchKind
{
    /// <summary>Each value is a glyph.</summary>
    Glyph = 0,

    /// <summary>Each value is a class of a class definition table.</summary>
    Class = 1,

    /// <summary>Each value is the offset of a coverage table.</summary>
    Coverage = 2,
}
