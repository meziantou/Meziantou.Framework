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
    /// <param name="strideInBytes">The distance between rows (0 = tightly packed); the bytes between rows are set to <paramref name="padding"/>. The last row has no trailing padding.</param>
    /// <param name="padding">The padding value.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ToPixelBytes(RawPixelBuffer source, PixelFormat format, int strideInBytes = 0, byte padding = DefaultPadding)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureLayout(source, format);
        var rowBytes = source.RowBytes;
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

    private static TPixel CreatePixel<TPixel>(RawPixelBuffer source, int x, int y)
        where TPixel : unmanaged
    {
        int S(int channel) => source.GetSample(x, y, channel);
        object pixel = PixelFormats.GetPixelFormat<TPixel>() switch
        {
            PixelFormat.Rgba32 => new Rgba32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            PixelFormat.Bgra32 => new Bgra32((byte)S(0), (byte)S(1), (byte)S(2), (byte)S(3)),
            PixelFormat.Rgb24 => new Rgb24((byte)S(0), (byte)S(1), (byte)S(2)),
            PixelFormat.Rgba64 => new Rgba64((ushort)S(0), (ushort)S(1), (ushort)S(2), (ushort)S(3)),
            PixelFormat.Gray8 => new Gray8((byte)S(0)),
            _ => new Gray16((ushort)S(0)),
        };

        return (TPixel)pixel;
    }

    private static void EnsureLayout(RawPixelBuffer source, PixelFormat format)
    {
        var expected = ImageSnapshots.GetLayout(format);
        if (source.Layout != expected)
            throw new ArgumentException($"A {format} import needs a {expected} reference, got {source.Layout}.", nameof(source));
    }
}
