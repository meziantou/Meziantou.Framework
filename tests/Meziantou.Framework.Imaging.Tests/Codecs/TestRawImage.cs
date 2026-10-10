using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>Encodes inputs of the <see cref="TestRawCodec"/> format.</summary>
internal static class TestRawImage
{
    /// <summary>Encodes an image whose frames are given as raw rows of <paramref name="source"/>.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="source">The layout of the encoded rows.</param>
    /// <param name="defaultFormat">The default working representation reported by the codec.</param>
    /// <param name="frames">Each frame: duration and tightly packed pixel bytes.</param>
    /// <param name="poster">The optional poster pixel bytes.</param>
    /// <param name="totalPlays">The total plays (0 = infinite).</param>
    /// <param name="iccColorSpace">0: no profile, 1: GRAY profile, 2: RGB profile.</param>
    /// <param name="animated">Whether the file declares animation settings even with one frame.</param>
    /// <param name="unsupportedFeature">Whether the header declares an unsupported feature.</param>
    /// <returns>The encoded bytes.</returns>
    public static byte[] Encode(int width, int height, PixelFormat source, PixelFormat defaultFormat, IReadOnlyList<(FrameDuration Duration, byte[] Pixels)> frames, byte[]? poster = null, int totalPlays = 0, byte iccColorSpace = 0, bool animated = false, bool unsupportedFeature = false)
    {
        using var output = new MemoryStream();
        output.Write(TestRawCodec.Signature);
        Span<byte> header = stackalloc byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(header, width);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], height);
        header[8] = (byte)source;
        header[9] = (byte)defaultFormat;
        header[10] = (byte)frames.Count;
        header[11] = (byte)((poster is null ? 0 : 1) | (animated ? 2 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)totalPlays);
        header[14] = iccColorSpace;
        header[15] = unsupportedFeature ? (byte)1 : (byte)0;
        output.Write(header);
        if (poster is not null)
        {
            WriteFrame(output, FrameDuration.Zero, poster, width, height, source);
        }

        foreach (var (duration, pixels) in frames)
        {
            WriteFrame(output, duration, pixels, width, height, source);
        }

        return output.ToArray();
    }

    /// <summary>Creates frame pixels whose bytes follow a deterministic pattern seeded by <paramref name="seed"/>.</summary>
    public static byte[] Pattern(int width, int height, PixelFormat format, int seed)
    {
        var data = new byte[width * height * PixelFormats.GetBytesPerPixel(format)];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 37) + (seed * 101) + 11);
        }

        if (PixelFormats.HasAlpha(format))
        {
            // Opaque unless the caller makes pixels translucent
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
            var alphaSize = PixelFormats.GetBitsPerComponent(format) / 8;
            for (var offset = 0; offset < data.Length; offset += bytesPerPixel)
            {
                data.AsSpan(offset + bytesPerPixel - alphaSize, alphaSize).Fill(0xFF);
            }
        }

        return data;
    }

    /// <summary>Creates a 132-byte ICC header with the specified data color space (the codec does not validate it).</summary>
    public static byte[] CreateIccHeader(ReadOnlySpan<byte> colorSpace)
    {
        var data = new byte[132];
        BinaryPrimitives.WriteUInt32BigEndian(data, 132);
        colorSpace.CopyTo(data.AsSpan(16));
        "acsp"u8.CopyTo(data.AsSpan(36));
        return data;
    }

    /// <summary>Creates a registry containing the test codec in front of the built-in codecs.</summary>
    public static ImageCodecRegistry CreateRegistry(TestRawCodec codec) => new([codec, .. ImageCodecRegistry.Default.Codecs]);

    private static void WriteFrame(MemoryStream output, FrameDuration duration, byte[] pixels, int width, int height, PixelFormat source)
    {
        if (pixels.Length != width * height * PixelFormats.GetBytesPerPixel(source))
            throw new ArgumentException("The frame has the wrong size.", nameof(pixels));

        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)duration.Numerator);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)duration.Denominator);
        output.Write(header);
        output.Write(pixels);
    }
}
