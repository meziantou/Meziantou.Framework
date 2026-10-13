namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>How a <see cref="FontBuilder"/> encodes the flags and the coordinates of a simple glyph.</summary>
internal enum FontGlyfEncoding
{
    /// <summary>Short vectors, "same" flags and repeated flags wherever they apply.</summary>
    Compact = 0,

    /// <summary>One flag per point and 16-bit coordinates.</summary>
    Long = 1,
}
