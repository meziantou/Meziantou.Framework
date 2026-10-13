using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads a font file into memory, within the limits of the configuration, and creates its faces.
/// </summary>
/// <remarks>
/// The whole file is read: the tables of a font are a graph of offsets, and the font keeps its bytes to read them in
/// place. The bytes read are bounded by <see cref="ImageResourceLimits.MaxEncodedBytes"/>, and the bytes the font keeps
/// by <see cref="ImageResourceLimits.MaxFontBytes"/>.
/// </remarks>
internal static class FontLoader
{
    private const int ChunkLength = 64 * 1024;

    public static FontFace[] Load(ReadOnlySpan<byte> data, FontLoadOptions options)
    {
        var configuration = options.Configuration;
        var limits = configuration.Limits;
        if (data.Length > limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, data.Length);

        EnsureFontBytes(limits, data.Length);
        return Parse(data.ToArray(), options);
    }

    public static FontFace[] Load(string path, FontLoadOptions options)
    {
        using var stream = ImageIO.OpenFile(path, asynchronous: false);
        return Load(stream, options);
    }

    public static FontFace[] Load(Stream stream, FontLoadOptions options)
    {
        var configuration = options.Configuration;
        var limits = configuration.Limits;
        var buffer = new byte[GetInitialLength(stream, limits)];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                buffer = Grow(buffer, limits);
            }

            var read = stream.Read(buffer.AsSpan(length));
            if (read == 0)
                break;

            length += read;
            EnsureEncodedBytes(limits, length);
        }

        return Parse(Trim(buffer, length), options);
    }

    public static async Task<FontFace[]> LoadAsync(string path, FontLoadOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = ImageIO.OpenFile(path, asynchronous: true);
        await using (stream.ConfigureAwait(false))
        {
            return await LoadAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<FontFace[]> LoadAsync(Stream stream, FontLoadOptions options, CancellationToken cancellationToken)
    {
        var configuration = options.Configuration;
        cancellationToken.ThrowIfCancellationRequested();
        var limits = configuration.Limits;
        var buffer = new byte[GetInitialLength(stream, limits)];
        var length = 0;
        while (true)
        {
            if (length == buffer.Length)
            {
                buffer = Grow(buffer, limits);
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            length += read;
            EnsureEncodedBytes(limits, length);
        }

        return Parse(Trim(buffer, length), options);
    }

    private static int GetInitialLength(Stream stream, ImageResourceLimits limits)
    {
        // The remaining length of a seekable stream avoids the copies of a growing buffer. It is only a hint: the
        // limits are enforced on the bytes actually read.
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining > limits.MaxEncodedBytes)
                throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, remaining);

            if (remaining is > 0 and < int.MaxValue)
                return (int)remaining + 1;
        }

        return ChunkLength;
    }

    private static byte[] Grow(byte[] buffer, ImageResourceLimits limits)
    {
        var length = Math.Min((long)buffer.Length * 2, Math.Min(limits.MaxEncodedBytes + 1, Array.MaxLength));
        if (length <= buffer.Length)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, (long)buffer.Length + 1);

        var result = new byte[length];
        buffer.CopyTo(result, 0);
        return result;
    }

    private static byte[] Trim(byte[] buffer, int length) => length == buffer.Length ? buffer : buffer.AsSpan(0, length).ToArray();

    private static void EnsureEncodedBytes(ImageResourceLimits limits, long length)
    {
        if (length > limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, length);
    }

    private static void EnsureFontBytes(ImageResourceLimits limits, long length)
    {
        if (length > limits.MaxFontBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, limits.MaxFontBytes, length);
    }

    /// <summary>Creates the faces of a font file. The faces keep the bytes of a font; a compressed container is expanded first.</summary>
    private static FontFace[] Parse(byte[] bytes, FontLoadOptions options)
    {
        var configuration = options.Configuration;
        if (bytes.Length < 4)
            throw new UnknownImageFormatException("The data is not a font: it is shorter than a font signature.");

        var limits = configuration.Limits;
        switch (FontBinary.UInt32(bytes, 0))
        {
            case FontTags.Woff:
                bytes = WoffReader.Decode(bytes, limits);
                break;

            case FontTags.Woff2:
                bytes = Woff2Reader.Decode(bytes, configuration);
                break;
        }

        // A container holds a font, never another container.
        EnsureFontBytes(limits, bytes.Length);
        switch (bytes.Length < 4 ? 0 : FontBinary.UInt32(bytes, 0))
        {
            case FontTags.TrueType or FontTags.AppleTrueType or FontTags.OpenTypeCff or FontTags.PostScriptType1:
                return [new FontFace(new FontData(bytes, configuration, options.UseEmbeddedBitmaps), directoryOffset: 0)];

            case FontTags.Collection:
                return ParseCollection(new FontData(bytes, configuration, options.UseEmbeddedBitmaps));

            case FontTags.Woff or FontTags.Woff2:
                throw FontErrors.Invalid("a WOFF file contains another WOFF file.");

            default:
                throw new UnknownImageFormatException("The data is not a font: it does not start with the signature of a TrueType, OpenType, WOFF or WOFF2 font or of a font collection.");
        }
    }

    /// <summary>Reads the header of a font collection: OpenType specification, "Font Collections".</summary>
    private static FontFace[] ParseCollection(FontData data)
    {
        var file = data.Bytes.AsSpan();
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
