namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads a font file into memory, within the limits of an environment, and creates its faces.
/// </summary>
/// <remarks>
/// The whole file is read: the tables of a font are a graph of offsets, and the font keeps its bytes to read them in
/// place. The bytes read are bounded by <see cref="FontResourceLimits.MaxEncodedBytes"/>, and the bytes the font keeps
/// by <see cref="FontResourceLimits.MaxFontBytes"/>.
/// </remarks>
public static class FontLoader
{
    /// <param name="data">The bytes of the font file.</param>
    /// <param name="environment">The limits and the storage of the font code.</param>
    /// <param name="useEmbeddedBitmaps">Whether the bitmaps without colors of the font replace its outlines at the sizes they are made for.</param>
    public static FontFace[] Load(ReadOnlySpan<byte> data, FontEnvironment environment, bool useEmbeddedBitmaps = false)
    {
        var limits = environment.Limits;
        if (data.Length > limits.MaxEncodedBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, data.Length);

        EnsureFontBytes(limits, data.Length);

        // A WOFF2 file is expanded from the span: only the font it holds is kept, not a copy of the file.
        if (data.Length >= 4 && FontBinary.UInt32(data, 0) == FontTags.Woff2)
            return ParseExpanded(Woff2Reader.Decode(data, environment), environment, useEmbeddedBitmaps);

        return Parse(data.ToArray(), environment, useEmbeddedBitmaps);
    }

    /// <summary>Reads the faces of a font file from a stream.</summary>
    /// <param name="stream">The stream of the font file, read to its end.</param>
    /// <param name="environment">The limits and the storage of the font code.</param>
    /// <param name="useEmbeddedBitmaps">Whether the bitmaps without colors of the font replace its outlines at the sizes they are made for.</param>
    /// <returns>The faces of the file: one for a font, one per font of a collection.</returns>
    public static FontFace[] Load(Stream stream, FontEnvironment environment, bool useEmbeddedBitmaps = false)
        => Parse(FontStreamReader.ReadAll(stream, environment.Limits), environment, useEmbeddedBitmaps);

    /// <summary>Reads the faces of a font file from a stream, asynchronously.</summary>
    /// <param name="stream">The stream of the font file, read to its end.</param>
    /// <param name="environment">The limits and the storage of the font code.</param>
    /// <param name="useEmbeddedBitmaps">Whether the bitmaps without colors of the font replace its outlines at the sizes they are made for.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The faces of the file: one for a font, one per font of a collection.</returns>
    public static async Task<FontFace[]> LoadAsync(Stream stream, FontEnvironment environment, bool useEmbeddedBitmaps, CancellationToken cancellationToken)
    {
        var bytes = await FontStreamReader.ReadAllAsync(stream, environment.Limits, cancellationToken).ConfigureAwait(false);
        return Parse(bytes, environment, useEmbeddedBitmaps);
    }

    private static void EnsureFontBytes(FontResourceLimits limits, long length)
    {
        if (length > limits.MaxFontBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, length);
    }

    /// <summary>Creates the faces of a font file. The faces keep the bytes of a font; a compressed container is expanded first.</summary>
    private static FontFace[] Parse(byte[] bytes, FontEnvironment environment, bool useEmbeddedBitmaps)
    {
        if (bytes.Length < 4)
            throw new UnknownFontFormatException("The data is not a font: it is shorter than a font signature.");

        switch (FontBinary.UInt32(bytes, 0))
        {
            case FontTags.Woff:
                bytes = WoffReader.Decode(bytes, environment.Limits);
                break;

            case FontTags.Woff2:
                bytes = Woff2Reader.Decode(bytes, environment);
                break;
        }

        return ParseExpanded(bytes, environment, useEmbeddedBitmaps);
    }

    /// <summary>Creates the faces of a font file that is not a compressed container, or of the font a container held.</summary>
    private static FontFace[] ParseExpanded(byte[] bytes, FontEnvironment environment, bool useEmbeddedBitmaps)
    {
        // A container holds a font, never another container.
        var limits = environment.Limits;
        EnsureFontBytes(limits, bytes.Length);
        switch (bytes.Length < 4 ? 0 : FontBinary.UInt32(bytes, 0))
        {
            case FontTags.TrueType or FontTags.AppleTrueType or FontTags.OpenTypeCff or FontTags.PostScriptType1:
                return [new FontFace(new FontData(bytes, environment, useEmbeddedBitmaps), directoryOffset: 0)];

            case FontTags.Collection:
                return ParseCollection(new FontData(bytes, environment, useEmbeddedBitmaps));

            case FontTags.Woff or FontTags.Woff2:
                throw FontErrors.Invalid("a WOFF file contains another WOFF file.");

            default:
                if (AfmParser.HasAfmSignature(bytes))
                    throw new UnknownFontFormatException($"The data is not a font: it is an AFM file (Adobe Font Metrics), which has metrics but no glyphs. Read it with {nameof(AfmFontMetrics)}.");

                throw new UnknownFontFormatException("The data is not a font: it does not start with the signature of a TrueType, OpenType, WOFF or WOFF2 font or of a font collection.");
        }
    }

    /// <summary>Reads the header of a font collection: OpenType specification, "Font Collections".</summary>
    private static FontFace[] ParseCollection(FontData data)
    {
        var file = data.RawBytes.AsSpan();
        var majorVersion = FontBinary.UInt16(file, 4);
        if (majorVersion is not (1 or 2))
            throw FontErrors.Invalid($"the font collection has the unknown version {majorVersion}.");

        var count = FontBinary.Offset32(file, 8);
        if (count == 0)
            throw FontErrors.Invalid("the font collection has no font.");

        var offsets = FontBinary.Slice(file, 12, (long)count * 4);
        var faces = new FontFace[count];
        for (var i = 0; i < count; i++)
        {
            faces[i] = new FontFace(data, FontBinary.UInt32(offsets, i * 4));
        }

        return faces;
    }
}
