using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Adam7 pass geometry of the W3C PNG specification, section 8.2, shared by the decoder and the encoder: pass
/// <c>p</c> holds the pixels at <c>(x0 + i * dx, y0 + j * dy)</c>. A pass whose width or height is zero has no scanline at
/// all (not even a filter byte).
/// </summary>
internal static class PngInterlace
{
    /// <summary>The number of Adam7 passes.</summary>
    public const int PassCount = 7;

    // x0, y0, dx, dy of each pass
    private static ReadOnlySpan<byte> Adam7 => [0, 0, 8, 8, 4, 0, 8, 8, 0, 4, 4, 8, 2, 0, 4, 4, 0, 2, 2, 4, 1, 0, 2, 2, 0, 1, 1, 2];

    // The pass of each pixel of a block of 8x8 pixels, row by row
    private static ReadOnlySpan<byte> PixelPasses =>
    [
        0, 5, 3, 5, 1, 5, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        4, 5, 4, 5, 4, 5, 4, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        2, 5, 3, 5, 2, 5, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        4, 5, 4, 5, 4, 5, 4, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
    ];

    // The width and the height of the blocks of pixels that the passes up to each pass hold one pixel of
    private static ReadOnlySpan<byte> BlockSizes => [8, 8, 4, 8, 4, 4, 2, 4, 2, 2, 1, 2, 1, 1];

    /// <summary>Gets the origin and the steps of an Adam7 pass.</summary>
    /// <param name="pass">The pass index, 0 to 6.</param>
    /// <returns>The first column and row and the column and row steps.</returns>
    public static (int X0, int Y0, int Dx, int Dy) GetPass(int pass)
    {
        var adam7 = Adam7.Slice(pass * 4, 4);
        return (adam7[0], adam7[1], adam7[2], adam7[3]);
    }

    /// <summary>Gets the size of an Adam7 pass of an image.</summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="pass">The pass index, 0 to 6.</param>
    /// <returns>The pass size; <c>(0, 0)</c> for an empty pass.</returns>
    public static (int Width, int Height) GetPassSize(int width, int height, int pass)
    {
        var (x0, y0, dx, dy) = GetPass(pass);
        var passWidth = width > x0 ? ((width - x0 - 1) / dx) + 1 : 0;
        var passHeight = height > y0 ? ((height - y0 - 1) / dy) + 1 : 0;
        return passWidth == 0 || passHeight == 0 ? (0, 0) : (passWidth, passHeight);
    }

    /// <summary>Gets the pass that holds a pixel.</summary>
    /// <param name="x">The column of the pixel.</param>
    /// <param name="y">The row of the pixel.</param>
    /// <returns>The pass index, 0 to 6.</returns>
    public static int GetPassOfPixel(int x, int y) => PixelPasses[((y & 7) << 3) | (x & 7)];

    /// <summary>
    /// Gets the size of the blocks of pixels that the first passes hold one pixel of: the pixel at the top left of each
    /// block.
    /// </summary>
    /// <param name="passCount">The number of passes, 1 to 7.</param>
    /// <returns>The width and the height of a block.</returns>
    public static (int Width, int Height) GetBlockSize(int passCount)
    {
        var size = BlockSizes.Slice((passCount - 1) * 2, 2);
        return (size[0], size[1]);
    }

    /// <summary>
    /// Copies pixels between two strided sequences: pixel <c>i</c> of the source, <c>i * sourceStep</c> pixels from its
    /// start, goes to pixel <c>i * destinationStep</c> of the destination (the scattering of a pass row into an image row,
    /// or the gathering of the pixels of a pass). The usual pixel sizes are one typed load and store per pixel.
    /// </summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="sourceStep">The distance between two source pixels, in pixels.</param>
    /// <param name="destination">The destination pixels.</param>
    /// <param name="destinationStep">The distance between two destination pixels, in pixels.</param>
    /// <param name="count">The number of pixels.</param>
    /// <param name="bytesPerPixel">The size of a pixel, in bytes.</param>
    public static void CopyPixels(ReadOnlySpan<byte> source, int sourceStep, Span<byte> destination, int destinationStep, int count, int bytesPerPixel)
    {
        if (count <= 0)
            return;

        // The last pixel of each sequence is within its span: every access of the loops is
        source = source[..((((count - 1) * sourceStep) + 1) * bytesPerPixel)];
        destination = destination[..((((count - 1) * destinationStep) + 1) * bytesPerPixel)];
        if (sourceStep == 1 && destinationStep == 1)
        {
            // Contiguous pixels (the last pass of a row)
            source.CopyTo(destination);
            return;
        }

        ref var from = ref unsafe(MemoryMarshal.GetReference(source));
        ref var to = ref unsafe(MemoryMarshal.GetReference(destination));
        var sourceStride = (nint)sourceStep * bytesPerPixel;
        var destinationStride = (nint)destinationStep * bytesPerPixel;
        switch (bytesPerPixel)
        {
            case 1:
                CopyPixels<byte>(ref from, sourceStride, ref to, destinationStride, count);
                break;
            case 2:
                CopyPixels<ushort>(ref from, sourceStride, ref to, destinationStride, count);
                break;
            case 3:
                CopyPixels3(ref from, sourceStride, ref to, destinationStride, count);
                break;
            case 4:
                CopyPixels<uint>(ref from, sourceStride, ref to, destinationStride, count);
                break;
            case 6:
                CopyPixels6(ref from, sourceStride, ref to, destinationStride, count);
                break;
            case 8:
                CopyPixels<ulong>(ref from, sourceStride, ref to, destinationStride, count);
                break;
            default:
                CopyPixelsScalar(source, sourceStep, destination, destinationStep, count, bytesPerPixel);
                break;
        }
    }

    /// <summary>The pixel-at-a-time reference of <see cref="CopyPixels"/>.</summary>
    internal static void CopyPixelsScalar(ReadOnlySpan<byte> source, int sourceStep, Span<byte> destination, int destinationStep, int count, int bytesPerPixel)
    {
        for (var i = 0; i < count; i++)
        {
            source.Slice(i * sourceStep * bytesPerPixel, bytesPerPixel).CopyTo(destination[(i * destinationStep * bytesPerPixel)..]);
        }
    }

    /// <summary>Copies one pixel: <paramref name="source"/> and <paramref name="destination"/> start at the pixel.</summary>
    /// <param name="source">The source pixel and what follows it.</param>
    /// <param name="destination">The destination pixel and what follows it.</param>
    /// <param name="bytesPerPixel">The size of a pixel, in bytes.</param>
    public static void CopyPixel(ReadOnlySpan<byte> source, Span<byte> destination, int bytesPerPixel)
    {
        source = source[..bytesPerPixel];
        destination = destination[..bytesPerPixel];
        ref var from = ref unsafe(MemoryMarshal.GetReference(source));
        ref var to = ref unsafe(MemoryMarshal.GetReference(destination));
        switch (bytesPerPixel)
        {
            case 1:
                to = from;
                break;
            case 2:
                unsafe { Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<ushort>(ref from)); }
                break;
            case 3:
                unsafe
                {
                    Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<ushort>(ref from));
                    Unsafe.Add(ref to, 2) = Unsafe.Add(ref from, 2);
                }

                break;
            case 4:
                unsafe { Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<uint>(ref from)); }
                break;
            case 6:
                unsafe
                {
                    Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<uint>(ref from));
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, 4), Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref from, 4)));
                }

                break;
            case 8:
                unsafe { Unsafe.WriteUnaligned(ref to, Unsafe.ReadUnaligned<ulong>(ref from)); }
                break;
            default:
                source.CopyTo(destination);
                break;
        }
    }

    private static void CopyPixels<T>(ref byte source, nint sourceStride, ref byte destination, nint destinationStride, int count)
        where T : unmanaged
    {
        for (var i = 0; i < count; i++)
        {
            unsafe
            {
                Unsafe.WriteUnaligned(ref destination, Unsafe.ReadUnaligned<T>(ref source));
                source = ref Unsafe.Add(ref source, sourceStride);
                destination = ref Unsafe.Add(ref destination, destinationStride);
            }
        }
    }

    private static void CopyPixels3(ref byte source, nint sourceStride, ref byte destination, nint destinationStride, int count)
    {
        for (var i = 0; i < count; i++)
        {
            unsafe
            {
                Unsafe.WriteUnaligned(ref destination, Unsafe.ReadUnaligned<ushort>(ref source));
                Unsafe.Add(ref destination, 2) = Unsafe.Add(ref source, 2);
                source = ref Unsafe.Add(ref source, sourceStride);
                destination = ref Unsafe.Add(ref destination, destinationStride);
            }
        }
    }

    private static void CopyPixels6(ref byte source, nint sourceStride, ref byte destination, nint destinationStride, int count)
    {
        for (var i = 0; i < count; i++)
        {
            unsafe
            {
                Unsafe.WriteUnaligned(ref destination, Unsafe.ReadUnaligned<uint>(ref source));
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 4), Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref source, 4)));
                source = ref Unsafe.Add(ref source, sourceStride);
                destination = ref Unsafe.Add(ref destination, destinationStride);
            }
        }
    }
}
