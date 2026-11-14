namespace Meziantou.Framework.Fonts;

/// <summary>
/// The bytes of a font file, kept for the lifetime of the fonts created from it and shared by the faces of a collection.
/// </summary>
/// <remarks>
/// The tables are read in place, never copied. The array is an exact garbage-collected array, bounded by
/// <see cref="FontResourceLimits.MaxFontBytes"/> when it is created: a font is an immutable object without owner or
/// lifetime, so its bytes cannot be rented from a pool.
/// </remarks>
public sealed class FontData
{
    private readonly WoffExtendedData? _woffExtendedData;

    internal FontData(byte[] bytes, FontEnvironment environment, bool useEmbeddedBitmaps = false, WoffExtendedData? woffExtendedData = null)
    {
        _woffExtendedData = woffExtendedData;
        RawBytes = bytes;
        Environment = environment;
        UseEmbeddedBitmaps = useEmbeddedBitmaps;
    }

    /// <summary>Gets the bytes of the font, in the sfnt format: a compressed container is expanded.</summary>
    public ReadOnlyMemory<byte> Bytes => RawBytes;

    /// <summary>Gets the array of <see cref="Bytes"/>, which the tables are read from in place.</summary>
    internal byte[] RawBytes { get; }

    /// <summary>Gets the environment the font was loaded with.</summary>
    public FontEnvironment Environment { get; }

    /// <summary>Gets a value indicating whether the bitmaps without colors of the font replace its outlines at the sizes they are made for.</summary>
    public bool UseEmbeddedBitmaps { get; }

    /// <summary>Gets the limits of the environment the font was loaded with: they bound the work of decoding a glyph and the size of a text.</summary>
    public FontResourceLimits Limits => Environment.Limits;

    /// <summary>Gets the extended metadata of the WOFF or WOFF2 file the font was read from.</summary>
    /// <returns>
    /// The XML document of the metadata block, or <see langword="null"/> when the font was not read from a WOFF or WOFF2
    /// file or when the file has no metadata block. The document is not parsed nor validated.
    /// </returns>
    /// <remarks>The block is kept compressed with the font and expanded at each call, in a buffer rented from <see cref="Environment"/>.</remarks>
    /// <exception cref="InvalidFontException">The metadata block lies outside the file or its compressed data is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The document is longer than <see cref="FontResourceLimits.MaxFontBytes"/> or than the temporary buffers of the environment can hold.</exception>
    public string? GetWoffMetadata() => _woffExtendedData?.GetMetadata(Environment);

    /// <summary>Gets the private data block of the WOFF or WOFF2 file the font was read from.</summary>
    /// <returns>The bytes of the block, which only the font vendor can interpret, or an empty value when the font was not read from a WOFF or WOFF2 file or when the file has no private data block.</returns>
    /// <exception cref="InvalidFontException">The private data block lies outside the file.</exception>
    public ReadOnlyMemory<byte> GetWoffPrivateData() => _woffExtendedData?.GetPrivateData() ?? default;
}
