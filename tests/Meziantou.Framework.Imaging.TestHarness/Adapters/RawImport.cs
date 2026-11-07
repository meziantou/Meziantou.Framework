using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Adapters;

/// <summary>
/// Turns independent reference buffers into the caller-memory layouts accepted by the copied raw imports
/// (<see cref="Image.ImportPixelBytes{TPixel}(ReadOnlySpan{byte}, int, int, int, ImageConfiguration?)"/>,
/// <see cref="Image.ImportPixelData{TPixel}(ReadOnlySpan{TPixel}, int, int, int, ImageConfiguration?)"/>), with optional
/// row padding. The transforms are pure byte/sample reorders written here, not library conversions.
/// </summary>
public static class RawImport
{
    /// <summary>The default value written in padding bytes, so that a padding byte read as a pixel is visible in diffs.</summary>
    public const byte DefaultPadding = 0xA5;

    /// <summary>Lays a reference buffer out in the native byte layout of <paramref name="format"/> (BGRA order for <c>Bgra32</c>, native-endian 16-bit samples).</summary>
    /// <param name="source">The reference, in the layout <see cref="ImageSnapshots.GetLayout(PixelFormat)"/> of <paramref name="format"/>.</param>
    /// <param name="format">The pixel format of the import.</param>
    /// <param name="strideInBytes">The distance between rows (0 = tightly packed), at least the row length of <paramref name="format"/>; the bytes between rows are set to <paramref name="padding"/>. The last row has no trailing padding.</param>
    /// <param name="padding">The padding value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ToPixelBytes(RawPixelBuffer source, PixelFormat format, int strideInBytes = 0, byte padding = DefaultPadding)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureLayout(source, format);
        var rowBytes = source.Width * PixelFormats.GetBytesPerPixel(format);
        var stride = strideInBytes == 0 ? rowBytes : strideInBytes;
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, rowBytes, nameof(strideInBytes));
        var result = new byte[(stride * (source.Height - 1)) + rowBytes];
        result.AsSpan().Fill(padding);
        for (var y = 0; y < source.Height; y++)
        {
            var input = source.GetRow(y);
            var output = result.AsSpan(y * stride, rowBytes);
            switch (format)
            {
                case PixelFormat.Bgra32:
                    for (var i = 0; i < rowBytes; i += 4)
                    {
                        output[i] = input[i + 2];
                        output[i + 1] = input[i + 1];
                        output[i + 2] = input[i];
                        output[i + 3] = input[i + 3];
                    }

                    break;

                case PixelFormat.Rgba64 or PixelFormat.Gray16:
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(output));
                    for (var i = 0; i < samples.Length; i++)
                    {
                        samples[i] = BinaryPrimitives.ReadUInt16LittleEndian(input[(i * 2)..]);
                    }

                    break;

                case PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32:
                    // From the widened layout of ImageSnapshots.GetLayout: the stored components of each pixel
                    var width = source.Width;
                    var pixelSize = PixelFormats.GetBytesPerPixel(format);
                    for (var x = 0; x < width; x++)
                    {
                        WritePixel(format, CreatePixel(source, format, x, y), output.Slice(x * pixelSize, pixelSize));
                    }

                    break;

                default:
                    input.CopyTo(output);
                    break;
            }
        }

        return result;
    }

    /// <summary>Builds typed pixels from the samples of a reference buffer (independently of the byte layout of the pixel struct).</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="source">The reference, in the layout <see cref="ImageSnapshots.GetLayout(PixelFormat)"/> of <typeparamref name="TPixel"/>.</param>
    /// <param name="strideInPixels">The distance between rows (0 = tightly packed); padding pixels have every byte set to <paramref name="padding"/>.</param>
    /// <param name="padding">The padding byte value.</param>
    /// <returns>The pixels.</returns>
    public static TPixel[] ToPixelData<TPixel>(RawPixelBuffer source, int strideInPixels = 0, byte padding = DefaultPadding)
        where TPixel : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source);
        var format = PixelFormats.GetPixelFormat<TPixel>();
        EnsureLayout(source, format);
        var stride = strideInPixels == 0 ? source.Width : strideInPixels;
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, source.Width, nameof(strideInPixels));
        var result = new TPixel[(stride * (source.Height - 1)) + source.Width];
        unsafe { MemoryMarshal.AsBytes(result.AsSpan()).Fill(padding); }
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                result[(y * stride) + x] = CreatePixel<TPixel>(source, x, y);
            }
        }

        return result;
    }

    /// <summary>Removes the alpha channel of a fully opaque <c>rgba8</c> reference (the corpus has no <c>rgb8</c> references for color images).</summary>
    /// <param name="source">The opaque reference.</param>
    /// <returns>The <c>rgb8</c> buffer.</returns>
    /// <exception cref="ArgumentException">The reference is not <c>rgba8</c> or has a non-opaque pixel.</exception>
    public static RawPixelBuffer DropOpaqueAlpha(RawPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Layout != RawPixelLayout.Rgba8)
            throw new ArgumentException($"Expected an rgba8 reference, got {source.Layout}.", nameof(source));

        var builder = new RawPixelBufferBuilder(source.Width, source.Height, RawPixelLayout.Rgb8);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetSample(x, y, 3) != byte.MaxValue)
                    throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Pixel ({x},{y}) is not opaque."), nameof(source));

                for (var channel = 0; channel < 3; channel++)
                {
                    builder.SetSample(x, y, channel, source.GetSample(x, y, channel));
                }
            }
        }

        return builder.Build();
    }

    /// <summary>Determines whether every pixel of a reference is fully opaque (always true without alpha).</summary>
    /// <param name="source">The reference.</param>
    /// <returns><see langword="true"/> if opaque.</returns>
    public static bool IsOpaque(RawPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Layout.HasAlpha)
            return true;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetSample(x, y, source.Layout.AlphaChannel) != source.Layout.MaxSampleValue)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether every pixel of a reference is exactly black or exactly white: equal color samples that are 0 or
    /// the largest sample value, at the precision of the reference. Alpha is not looked at (see <see cref="IsOpaque"/>).
    /// </summary>
    /// <param name="source">The reference.</param>
    /// <returns><see langword="true"/> if the reference holds only black and white pixels.</returns>
    public static bool IsBlackAndWhite(RawPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var colorChannels = source.Layout.IsColor ? 3 : 1;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var value = source.GetSample(x, y, 0);
                if (value != 0 && value != source.Layout.MaxSampleValue)
                    return false;

                for (var channel = 1; channel < colorChannels; channel++)
                {
                    if (source.GetSample(x, y, channel) != value)
                        return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Determines whether a reference in the layout of <see cref="ImageSnapshots.GetLayout(PixelFormat)"/> holds pixels that
    /// <paramref name="format"/> stores: a gray level for the gray formats with alpha, opaque pixels for <c>Rgb48</c> and
    /// black for <c>A8</c> (always true for the other formats).
    /// </summary>
    /// <param name="source">The reference.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if every pixel can be stored.</returns>
    public static bool IsRepresentable(RawPixelBuffer source, PixelFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (format is not (PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8))
            return true;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (!IsRepresentablePixel(source, format, x, y))
                    return false;
            }
        }

        return true;
    }

    private static TPixel CreatePixel<TPixel>(RawPixelBuffer source, int x, int y)
        where TPixel : unmanaged
        => (TPixel)CreatePixel(source, PixelFormats.GetPixelFormat<TPixel>(), x, y);

    private static object CreatePixel(RawPixelBuffer source, PixelFormat format, int x, int y)
    {
        int S(int channel) => source.GetSample(x, y, channel);
        if (format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8 && !IsRepresentablePixel(source, format, x, y))
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Pixel ({x},{y}) ({S(0)}, {S(1)}, {S(2)}, {S(3)}) cannot be stored as {format}."), nameof(source));

        return format switch
        {
            PixelFormat.Rgba32 => new Rgba32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            PixelFormat.Bgra32 => new Bgra32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            PixelFormat.Rgb24 => new Rgb24((byte)S(0), (byte)S(1), (byte)S(2)),
            PixelFormat.Rgba64 => new Rgba64((ushort)S(0), (ushort)S(1), (ushort)S(2), (ushort)S(3)),
            PixelFormat.Gray8 => new Gray8((byte)S(0)),
            PixelFormat.Gray16 => new Gray16((ushort)S(0)),
            PixelFormat.La16 => new La16((byte)S(0), (byte)S(3)),
            PixelFormat.La32 => new La32((ushort)S(0), (ushort)S(3)),
            PixelFormat.Rgb48 => new Rgb48((ushort)S(0), (ushort)S(1), (ushort)S(2)),
            PixelFormat.A8 => new A8((byte)S(3)),
            PixelFormat.Argb32 => new Argb32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            PixelFormat.Abgr32 => new Abgr32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format."),
        };
    }

    private static bool IsRepresentablePixel(RawPixelBuffer source, PixelFormat format, int x, int y)
    {
        var (r, g, b, a) = (source.GetSample(x, y, 0), source.GetSample(x, y, 1), source.GetSample(x, y, 2), source.GetSample(x, y, 3));
        return format switch
        {
            PixelFormat.Rgb48 => a == source.Layout.MaxSampleValue,
            PixelFormat.A8 => (r | g | b) == 0,
            _ => r == g && g == b,
        };
    }

    private static void WritePixel(PixelFormat format, object pixel, Span<byte> destination)
    {
        switch (pixel)
        {
            case La16 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            case La32 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            case Rgb48 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            case A8 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            case Argb32 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            case Abgr32 p:
                unsafe { MemoryMarshal.Write(destination, p); }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown pixel format.");
        }
    }

    private static void EnsureLayout(RawPixelBuffer source, PixelFormat format)
    {
        var expected = ImageSnapshots.GetLayout(format);
        if (source.Layout != expected)
            throw new ArgumentException($"A {format} import needs a {expected} reference, got {source.Layout}.", nameof(source));
    }
}
