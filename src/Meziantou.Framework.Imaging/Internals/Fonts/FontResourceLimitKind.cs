namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The limits of <see cref="FontResourceLimits"/> and of a <see cref="FontAllocator"/>.</summary>
internal enum FontResourceLimitKind
{
    /// <summary>The bytes read from a font file (<see cref="FontResourceLimits.MaxEncodedBytes"/>).</summary>
    EncodedBytes,

    /// <summary>The bytes a font keeps (<see cref="FontResourceLimits.MaxFontBytes"/>).</summary>
    FontBytes,

    /// <summary>The elementary operations to produce one glyph (<see cref="FontResourceLimits.MaxGlyphComplexity"/>).</summary>
    GlyphComplexity,

    /// <summary>The glyphs of a shaped text (<see cref="FontResourceLimits.MaxTextGlyphs"/>).</summary>
    TextGlyphs,

    /// <summary>The bytes of the working storage rented from a <see cref="FontAllocator"/>.</summary>
    LiveAllocationBytes,
}
