namespace Meziantou.Framework.Fonts;

/// <summary>
/// The limits that bound the work of the font code on untrusted input: the bytes read and kept, the work of decoding a
/// glyph and the glyphs of a shaped text. Exceeding a limit throws a <see cref="FontResourceLimitException"/>.
/// </summary>
public sealed class FontResourceLimits
{
    /// <summary>The default maximum number of bytes read from a font file (256 MiB).</summary>
    public const long DefaultMaxEncodedBytes = 256L * 1024 * 1024;

    /// <summary>The default maximum number of bytes a font keeps in memory (256 MiB).</summary>
    public const long DefaultMaxFontBytes = 256L * 1024 * 1024;

    /// <summary>The default maximum number of elementary operations to produce one glyph (1,000,000).</summary>
    public const long DefaultMaxGlyphComplexity = 1_000_000;

    /// <summary>The default maximum number of glyphs of a shaped text (1,000,000).</summary>
    public const long DefaultMaxTextGlyphs = 1_000_000;

    /// <summary>Gets the default limits.</summary>
    public static FontResourceLimits Default { get; } = new();

    /// <summary>Gets the maximum number of bytes read from a font file.</summary>
    public long MaxEncodedBytes { get; init; } = DefaultMaxEncodedBytes;

    /// <summary>Gets the maximum number of bytes a font keeps in memory, after a compressed container is expanded.</summary>
    public long MaxFontBytes { get; init; } = DefaultMaxFontBytes;

    /// <summary>Gets the maximum number of elementary operations (outline points, components, operators) to produce one glyph.</summary>
    public long MaxGlyphComplexity { get; init; } = DefaultMaxGlyphComplexity;

    /// <summary>Gets the maximum number of glyphs of a shaped text.</summary>
    public long MaxTextGlyphs { get; init; } = DefaultMaxTextGlyphs;
}
