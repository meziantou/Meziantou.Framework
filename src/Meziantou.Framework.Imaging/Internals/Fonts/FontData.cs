namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bytes of a font file, kept for the lifetime of the fonts created from it and shared by the faces of a collection.
/// </summary>
/// <remarks>
/// The tables are read in place, never copied. The array is an exact garbage-collected array, bounded by
/// <see cref="ImageResourceLimits.MaxFontBytes"/> when it is created: a font is an immutable object without owner or
/// lifetime, like <see cref="Metadata.MetadataBlob"/>, so its bytes cannot be rented from the pool of an image.
/// </remarks>
internal sealed class FontData
{
    public FontData(byte[] bytes, ImageConfiguration configuration)
    {
        Bytes = bytes;
        Configuration = configuration;
    }

    public byte[] Bytes { get; }

    /// <summary>Gets the configuration the font was loaded with.</summary>
    public ImageConfiguration Configuration { get; }

    /// <summary>Gets the limits of the configuration the font was loaded with: they bound the work of decoding a glyph and the size of a text.</summary>
    public ImageResourceLimits Limits => Configuration.Limits;
}
