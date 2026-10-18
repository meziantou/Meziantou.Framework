namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bytes of a font file, kept for the lifetime of the fonts created from it and shared by the faces of a collection.
/// </summary>
/// <remarks>
/// The tables are read in place, never copied. The array is an exact garbage-collected array, bounded by
/// <see cref="FontResourceLimits.MaxFontBytes"/> when it is created: a font is an immutable object without owner or
/// lifetime, so its bytes cannot be rented from a pool.
/// </remarks>
internal sealed class FontData
{
    public FontData(byte[] bytes, FontEnvironment environment, bool useEmbeddedBitmaps = false)
    {
        Bytes = bytes;
        Environment = environment;
        UseEmbeddedBitmaps = useEmbeddedBitmaps;
    }

    public byte[] Bytes { get; }

    /// <summary>Gets the environment the font was loaded with.</summary>
    public FontEnvironment Environment { get; }

    /// <summary>Gets a value indicating whether the bitmaps without colors of the font replace its outlines at the sizes they are made for.</summary>
    public bool UseEmbeddedBitmaps { get; }

    /// <summary>Gets the limits of the environment the font was loaded with: they bound the work of decoding a glyph and the size of a text.</summary>
    public FontResourceLimits Limits => Environment.Limits;
}
