namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What the font says of a variation sequence: a base character followed by a variation selector.</summary>
internal enum CmapVariation
{
    /// <summary>The font does not support the sequence.</summary>
    NotFound = 0,

    /// <summary>The sequence is displayed with the default glyph of the base character.</summary>
    UseDefault = 1,

    /// <summary>The sequence has its own glyph.</summary>
    Glyph = 2,
}
