using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Bulk row converters between the six built-in pixel formats. This is the single conversion
/// implementation used by typed loading, <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>, encoders and
/// processing; callers that need the option/profile policy use <see cref="PixelConversionPlan"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every pair of formats has a direct kernel; no conversion is routed through <see cref="Rgba32"/> or any other 8-bit
/// intermediate, so every representable 16-bit value is preserved. All arithmetic is integer and is performed at the
/// precision of the <em>source</em> samples; the precision change is the last step:
/// </para>
/// <list type="number">
/// <item>Read the source components (gray replicates its value into R, G and B; a format without alpha reads as fully opaque).</item>
/// <item>Remove alpha when the destination has none: fully opaque pixels are kept as is; otherwise the pixel is flattened
/// onto the background, <c>out = (src * a + bg * (max - a) + max / 2) / max</c> (nearest; <c>max</c> is odd so no ties
/// exist), with the background reduced to the source precision first. Without a background, any non-opaque pixel is
/// rejected before anything is written to the destination row.</item>
/// <item>Color to gray: Rec. 709 luma on the encoded values, <c>Y = (2126 R + 7152 G + 722 B + 5000) / 10000</c>
/// (nearest, ties upward), clamped to <c>max</c>.</item>
/// <item>Precision change: 8 to 16 bits <c>v * 257</c> (exact); 16 to 8 bits <c>(v * 255 + 32767) / 65535</c> (nearest,
/// no ties exist). Adding alpha sets it to fully opaque.</item>
/// </list>
/// <para>
/// <see cref="ConvertRowScalar{TSource, TDestination}(ReadOnlySpan{TSource}, Span{TDestination}, Rgba64?, ImageFormat)"/> is the numerical
/// reference: vectorized kernels must produce bit-identical results and are tested against it. With 128-bit hardware
/// vectors, every pair of formats and the search for a non-opaque pixel process 16 bytes at a time: byte moves (channel
/// order, alpha added or dropped, gray replicated, 8-bit samples to 16 bits), the reduction of 16-bit samples to 8 bits,
/// luma and flattening use integer forms (or a <see cref="float"/> division that is checked or corrected) that give the
/// results of the scalar formulas for every input.
/// Source and destination rows must not overlap, unless they start at the same address and the two formats have the same
/// size (in-place conversion, for example <see cref="Rgba32"/> to <see cref="Bgra32"/>).
/// </para>
/// </remarks>
internal static class PixelConverter
{
    private const uint Max8 = byte.MaxValue;
    private const uint Max16 = ushort.MaxValue;

    /// <summary>Converts a row of pixels.</summary>
    /// <typeparam name="TSource">The source pixel type.</typeparam>
    /// <typeparam name="TDestination">The destination pixel type.</typeparam>
    /// <param name="source">The source pixels.</param>
    /// <param name="destination">The destination; must be at least as long as <paramref name="source"/>. Only the first <c>source.Length</c> pixels are written.</param>
    /// <param name="background">The opaque color used to flatten non-opaque pixels when the destination has no alpha, or <see langword="null"/> to reject them.</param>
    /// <exception cref="NotSupportedException">A pixel type is not a built-in pixel struct (thrown before anything is written).</exception>
    /// <exception cref="ArgumentException">The destination is too short, or the background is not fully opaque.</exception>
    /// <param name="format">The image format reported in exceptions, or <see cref="ImageFormat.Unknown"/>.</param>
    /// <exception cref="UnsupportedImageFeatureException">The conversion removes alpha, a pixel is not fully opaque and no background is supplied (nothing is written).</exception>
    public static void ConvertRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background = null, ImageFormat format = ImageFormat.Unknown)
        where TSource : unmanaged
        where TDestination : unmanaged
        => ConvertRow(source, destination, background, format, useVectors: UseVectors);

    /// <summary>The scalar reference implementation of <see cref="ConvertRow{TSource, TDestination}(ReadOnlySpan{TSource}, Span{TDestination}, Rgba64?, ImageFormat)"/>.</summary>
    /// <typeparam name="TSource">The source pixel type.</typeparam>
    /// <typeparam name="TDestination">The destination pixel type.</typeparam>
    /// <param name="source">The source pixels.</param>
    /// <param name="destination">The destination.</param>
    /// <param name="background">The flattening background, or <see langword="null"/>.</param>
    /// <param name="format">The image format reported in exceptions, or <see cref="ImageFormat.Unknown"/>.</param>
    public static void ConvertRowScalar<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background = null, ImageFormat format = ImageFormat.Unknown)
        where TSource : unmanaged
        where TDestination : unmanaged
        => ConvertRow(source, destination, background, format, useVectors: false);

    private static void ConvertRow<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background, ImageFormat format, bool useVectors)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        var sourceFormat = PixelFormats.GetPixelFormat<TSource>();
        var destinationFormat = PixelFormats.GetPixelFormat<TDestination>();
        if (destination.Length < source.Length)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination has {destination.Length} pixels but {source.Length} are required."), nameof(destination));

        ValidateBackground(background);

        if (typeof(TSource) == typeof(TDestination))
        {
            unsafe { MemoryMarshal.Cast<TSource, TDestination>(source).CopyTo(destination); }
            return;
        }

        var removesAlpha = PixelFormats.HasAlpha(sourceFormat) && !PixelFormats.HasAlpha(destinationFormat);
        if (removesAlpha && background is null)
        {
            var index = useVectors ? IndexOfNonOpaque(source) : IndexOfNonOpaqueScalar(source);
            if (index >= 0)
                throw CreateNonOpaqueException(sourceFormat, destinationFormat, index, format);
        }

        var flattens = removesAlpha && background is not null;
        var done = useVectors ? ConvertVector(source, destination, flattens ? background : null) : 0;
        ConvertCore(source[done..], destination[done..], flattens ? background : null);
    }

    /// <summary>Converts a row of pixels stored as raw native-endian bytes.</summary>
    /// <param name="sourceFormat">The source format.</param>
    /// <param name="source">The source bytes; the length must be a multiple of the source bytes per pixel.</param>
    /// <param name="destinationFormat">The destination format.</param>
    /// <param name="destination">The destination bytes; must hold at least as many pixels as <paramref name="source"/>.</param>
    /// <param name="background">The flattening background, or <see langword="null"/> to reject non-opaque pixels when alpha is removed.</param>
    /// <returns>The number of pixels converted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A format is not a supported pixel format.</exception>
    /// <exception cref="ArgumentException">A buffer length is invalid, or the background is not fully opaque.</exception>
    /// <param name="format">The image format reported in exceptions, or <see cref="ImageFormat.Unknown"/>.</param>
    /// <exception cref="UnsupportedImageFeatureException">Non-opaque pixels would be discarded (nothing is written).</exception>
    public static int ConvertRow(PixelFormat sourceFormat, ReadOnlySpan<byte> source, PixelFormat destinationFormat, Span<byte> destination, Rgba64? background = null, ImageFormat format = ImageFormat.Unknown)
    {
        var sourceBytesPerPixel = PixelFormats.GetBytesPerPixel(sourceFormat);
        var destinationBytesPerPixel = PixelFormats.GetBytesPerPixel(destinationFormat);
        if (source.Length % sourceBytesPerPixel != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The source length ({source.Length} bytes) is not a multiple of the {sourceFormat} pixel size ({sourceBytesPerPixel} bytes)."), nameof(source));

        var width = source.Length / sourceBytesPerPixel;
        if (destination.Length / destinationBytesPerPixel < width)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The destination has {destination.Length} bytes but {(long)width * destinationBytesPerPixel} are required."), nameof(destination));

        destination = destination[..(width * destinationBytesPerPixel)];
        switch (sourceFormat)
        {
            case PixelFormat.Rgba32:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Rgba32>(source)), destinationFormat, destination, background, format);
                break;
            case PixelFormat.Bgra32:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Bgra32>(source)), destinationFormat, destination, background, format);
                break;
            case PixelFormat.Rgb24:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Rgb24>(source)), destinationFormat, destination, background, format);
                break;
            case PixelFormat.Rgba64:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Rgba64>(source)), destinationFormat, destination, background, format);
                break;
            case PixelFormat.Gray8:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Gray8>(source)), destinationFormat, destination, background, format);
                break;
            default:
                ConvertFrom(unsafe(MemoryMarshal.Cast<byte, Gray16>(source)), destinationFormat, destination, background, format);
                break;
        }

        return width;
    }

    /// <summary>Gets a value indicating whether every pixel of a row is fully opaque (always true for formats without alpha).</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <param name="row">The pixels.</param>
    /// <returns><see langword="true"/> if no pixel has an alpha below the maximum.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    public static bool IsOpaque<TPixel>(ReadOnlySpan<TPixel> row)
        where TPixel : unmanaged
    {
        _ = PixelFormats.GetPixelFormat<TPixel>();
        return IndexOfNonOpaque(row) < 0;
    }

    /// <summary>Gets the index of the first pixel of a row that is not fully opaque.</summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="row">The row bytes (native endian).</param>
    /// <returns>The pixel index, or -1 if every pixel is fully opaque (always -1 for formats without alpha).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int IndexOfNonOpaque(PixelFormat format, ReadOnlySpan<byte> row) => format switch
    {
        PixelFormat.Rgba32 => IndexOfNonOpaque(unsafe(MemoryMarshal.Cast<byte, Rgba32>(row))),
        PixelFormat.Bgra32 => IndexOfNonOpaque(unsafe(MemoryMarshal.Cast<byte, Bgra32>(row))),
        PixelFormat.Rgba64 => IndexOfNonOpaque(unsafe(MemoryMarshal.Cast<byte, Rgba64>(row))),
        _ => PixelFormats.HasAlpha(format) ? throw new UnreachableException() : -1,
    };

    /// <summary>Gets a value indicating whether every pixel of a raw native-endian row is fully opaque.</summary>
    /// <param name="format">The pixel format.</param>
    /// <param name="row">The row bytes.</param>
    /// <returns><see langword="true"/> if no pixel has an alpha below the maximum.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsOpaque(PixelFormat format, ReadOnlySpan<byte> row) => IndexOfNonOpaque(format, row) < 0;

    /// <summary>Converts a 16-bit sample to 8 bits with nearest rounding: <c>(v * 255 + 32767) / 65535</c>.</summary>
    /// <param name="value">The 16-bit sample.</param>
    /// <returns>The 8-bit sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte To8Bit(uint value) => (byte)(((value * Max8) + (Max16 / 2)) / Max16);

    /// <summary>Converts an 8-bit sample to 16 bits with exact full-range expansion: <c>v * 257</c>.</summary>
    /// <param name="value">The 8-bit sample.</param>
    /// <returns>The 16-bit sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort To16Bit(uint value) => (ushort)(value * 257);

    /// <summary>Computes Rec. 709 luma on encoded samples, nearest with ties upward, clamped to <paramref name="max"/>.</summary>
    /// <param name="r">The red sample.</param>
    /// <param name="g">The green sample.</param>
    /// <param name="b">The blue sample.</param>
    /// <param name="max">The maximum sample value (255 or 65535).</param>
    /// <returns>The luma, at the precision of the inputs.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Luma(uint r, uint g, uint b, uint max)
    {
        // At most 10000 * 65535 + 5000 = 655,355,000: no overflow in 32 bits
        var y = ((2126 * r) + (7152 * g) + (722 * b) + 5000) / 10000;
        return Math.Min(y, max);
    }

    /// <summary>Flattens a straight-alpha sample onto an opaque background: <c>(s * a + bg * (max - a) + max / 2) / max</c>.</summary>
    /// <param name="sample">The sample.</param>
    /// <param name="background">The background sample (same precision).</param>
    /// <param name="alpha">The alpha (same precision).</param>
    /// <param name="max">The maximum sample value (255 or 65535).</param>
    /// <returns>The flattened sample.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Flatten(uint sample, uint background, uint alpha, uint max)
    {
        // At most 65535 * 65535 + 32767 = 4,294,868,992 < 2^32
        return ((sample * alpha) + (background * (max - alpha)) + (max / 2)) / max;
    }

    /// <summary>
    /// Gets a value indicating whether a color converts to a pixel format without any loss: alpha is kept or fully opaque,
    /// a gray format gets a gray color, and an 8-bit format gets samples that are exact 8-bit values (multiples of 257).
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if converting the color to <paramref name="format"/> and back gives the same color.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsExactlyRepresentable(Rgba64 color, PixelFormat format)
    {
        var hasAlpha = PixelFormats.HasAlpha(format);
        if (!hasAlpha && color.A != ushort.MaxValue)
            return false;

        if (PixelFormats.IsGrayscale(format) && (color.R != color.G || color.G != color.B))
            return false;

        if (PixelFormats.GetBitsPerComponent(format) == 16)
            return true;

        return color.R % 257 == 0 && color.G % 257 == 0 && color.B % 257 == 0 && (!hasAlpha || color.A % 257 == 0);
    }

    internal static void ValidateBackground(Rgba64? background)
    {
        if (background is { A: not ushort.MaxValue })
            throw new ArgumentException("The background color must be fully opaque.", nameof(background));
    }

    internal static UnsupportedImageFeatureException CreateNonOpaqueException(PixelFormat sourceFormat, PixelFormat destinationFormat, int index, ImageFormat format = ImageFormat.Unknown)
        => new(string.Create(CultureInfo.InvariantCulture, $"Converting {sourceFormat} pixels to {destinationFormat} would discard alpha: the pixel at index {index} of the row is not fully opaque. Provide an opaque background color (PixelConversionOptions.BackgroundColor) to flatten the pixels, or use a pixel type with alpha."), format, "Alpha removal");

    private static void ConvertFrom<TSource>(ReadOnlySpan<TSource> source, PixelFormat destinationFormat, Span<byte> destination, Rgba64? background, ImageFormat format)
        where TSource : unmanaged
    {
        switch (destinationFormat)
        {
            case PixelFormat.Rgba32:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Rgba32>(destination)), background, format);
                break;
            case PixelFormat.Bgra32:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Bgra32>(destination)), background, format);
                break;
            case PixelFormat.Rgb24:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Rgb24>(destination)), background, format);
                break;
            case PixelFormat.Rgba64:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Rgba64>(destination)), background, format);
                break;
            case PixelFormat.Gray8:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Gray8>(destination)), background, format);
                break;
            default:
                ConvertRow(source, unsafe(MemoryMarshal.Cast<byte, Gray16>(destination)), background, format);
                break;
        }
    }

    /// <summary>Gets the index of the first pixel of a row that is not fully opaque.</summary>
    /// <typeparam name="TPixel">The pixel type (a built-in pixel struct).</typeparam>
    /// <param name="row">The pixels.</param>
    /// <returns>The pixel index, or -1 if every pixel is fully opaque (always -1 for formats without alpha).</returns>
    /// <remarks>With 128-bit hardware vectors, whole vectors of opaque pixels are skipped; the result is the one of <see cref="IndexOfNonOpaqueScalar"/>.</remarks>
    public static int IndexOfNonOpaque<TPixel>(ReadOnlySpan<TPixel> row)
        where TPixel : unmanaged
    {
        var skipped = 0;
        if (UseVectors)
        {
            if (typeof(TPixel) == typeof(Rgba32) || typeof(TPixel) == typeof(Bgra32))
            {
                // Alpha is the last byte of both layouts: every other byte is forced to 0xFF
                skipped = CountOpaqueVectors(unsafe(MemoryMarshal.AsBytes(row)), Vector128.Create(0x00FFFFFFu).AsByte()) * (Vector128<byte>.Count / 4);
            }
            else if (typeof(TPixel) == typeof(Rgba64))
            {
                skipped = CountOpaqueVectors(unsafe(MemoryMarshal.AsBytes(row)), Vector128.Create(0x0000FFFFFFFFFFFFul).AsByte()) * (Vector128<byte>.Count / 8);
            }
        }

        var index = IndexOfNonOpaqueScalar(row[skipped..]);
        return index < 0 ? -1 : skipped + index;
    }

    /// <summary>The scalar reference of <see cref="IndexOfNonOpaque{TPixel}(ReadOnlySpan{TPixel})"/>.</summary>
    internal static int IndexOfNonOpaqueScalar<TPixel>(ReadOnlySpan<TPixel> row)
        where TPixel : unmanaged
    {
        if (typeof(TPixel) == typeof(Rgba32))
        {
            var pixels = unsafe(MemoryMarshal.Cast<TPixel, Rgba32>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A != byte.MaxValue)
                    return i;
            }
        }
        else if (typeof(TPixel) == typeof(Bgra32))
        {
            var pixels = unsafe(MemoryMarshal.Cast<TPixel, Bgra32>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A != byte.MaxValue)
                    return i;
            }
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            var pixels = unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(row));
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].A != ushort.MaxValue)
                    return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Exchanges the first and the third byte of every 3-byte pixel (RGB to BGR and back), as the files that store blue first
    /// need. <paramref name="destination"/> may be <paramref name="source"/> itself (in-place exchange); the rows must not
    /// overlap otherwise.
    /// </summary>
    /// <param name="source">The pixels; the length is a multiple of 3.</param>
    /// <param name="destination">At least as many bytes as <paramref name="source"/>.</param>
    public static void SwapRedAndBlue24(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        destination = destination[..source.Length];
        var done = 0;
        if (UseVectors)
        {
            // 4 pixels per step; the last 4 bytes of a vector are stored unchanged and belong to the next step
            ref var input = ref unsafe(MemoryMarshal.GetReference(source));
            ref var output = ref unsafe(MemoryMarshal.GetReference(destination));
            var order = Vector128.Create((byte)2, 1, 0, 5, 4, 3, 8, 7, 6, 11, 10, 9, 12, 13, 14, 15);
            for (; done + Vector128<byte>.Count <= source.Length; done += 12)
            {
                unsafe { Vector128.Shuffle(Vector128.LoadUnsafe(ref input, (nuint)done), order).StoreUnsafe(ref output, (nuint)done); }
            }
        }

        SwapRedAndBlue24Scalar(source[done..], destination[done..]);
    }

    /// <summary>The scalar reference of <see cref="SwapRedAndBlue24"/>.</summary>
    internal static void SwapRedAndBlue24Scalar(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        for (var offset = 0; offset + 3 <= source.Length; offset += 3)
        {
            var first = source[offset];
            var third = source[offset + 2];
            destination[offset] = third;
            destination[offset + 1] = source[offset + 1];
            destination[offset + 2] = first;
        }
    }

    /// <summary>Gets a value indicating whether the vector paths are used (128-bit hardware vectors, little-endian lanes).</summary>
    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    /// <summary>Counts the leading whole vectors whose alpha bytes (the bytes cleared in <paramref name="colorMask"/>) are all 0xFF.</summary>
    private static int CountOpaqueVectors(ReadOnlySpan<byte> bytes, Vector128<byte> colorMask)
    {
        ref var start = ref unsafe(MemoryMarshal.GetReference(bytes));
        var count = 0;
        for (; ((count + 1) * Vector128<byte>.Count) <= bytes.Length; count++)
        {
            var pixels = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(count * Vector128<byte>.Count)));
            if ((pixels | colorMask) != Vector128<byte>.AllBitsSet)
                break;
        }

        return count;
    }

    /// <summary>
    /// Converts the leading pixels of a row with 128-bit vectors: the conversions that only move bytes (channel order, added
    /// opaque alpha, dropped alpha, replicated gray, 8-bit samples repeated into 16 bits), reduce 16-bit samples to 8 bits
    /// (<see cref="To8BitVector"/>), compute luma (<see cref="Luma8Vector"/>, <see cref="Luma16Vector"/>) or flatten
    /// (<see cref="Flatten8Vector"/>, <see cref="Flatten16Vector"/>).
    /// </summary>
    /// <param name="source">The source pixels.</param>
    /// <param name="destination">The destination pixels.</param>
    /// <param name="background">The background to flatten onto (alpha is removed and pixels may be non-opaque), or <see langword="null"/>.</param>
    /// <returns>The number of pixels converted; the scalar reference converts the others.</returns>
    /// <remarks>
    /// Without a background, alpha is only dropped once the caller has checked that it is opaque. A vector that holds 12
    /// meaningful bytes is read or written as 16 bytes while 16 bytes remain in the row. Flattening applies to every pixel:
    /// for an opaque pixel it gives the sample back (<c>(s * max + max / 2) / max = s</c>), as the scalar reference that
    /// skips opaque pixels.
    /// </remarks>
    private static int ConvertVector<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        ref var input = ref unsafe(Unsafe.As<TSource, byte>(ref MemoryMarshal.GetReference(source)));
        ref var output = ref unsafe(Unsafe.As<TDestination, byte>(ref MemoryMarshal.GetReference(destination)));
        var length = source.Length;
        var i = 0;
        var sourceIs32 = typeof(TSource) == typeof(Rgba32) || typeof(TSource) == typeof(Bgra32);
        var destinationIs32 = typeof(TDestination) == typeof(Rgba32) || typeof(TDestination) == typeof(Bgra32);
        var swaps = (typeof(TSource) == typeof(Bgra32)) != (typeof(TDestination) == typeof(Bgra32));
        var opaque = Vector128.Create(0xFF000000u).AsByte();

        // The background at the source precision, in the order of the source components (and 0 for alpha)
        var flattens = background is not null;
        Vector128<ushort> background8 = default;
        Vector128<uint> background16 = default;
        if (background is { } color)
        {
            if (typeof(TSource) == typeof(Rgba64))
            {
                background16 = Vector128.Create(color.R, color.G, color.B, 0u);
            }
            else
            {
                var (first, third) = typeof(TSource) == typeof(Bgra32) ? (color.B, color.R) : (color.R, color.B);
                var (c0, c1, c2) = ((ushort)To8Bit(first), (ushort)To8Bit(color.G), (ushort)To8Bit(third));
                background8 = Vector128.Create(c0, c1, c2, 0, c0, c1, c2, 0);
            }
        }

        if (sourceIs32 && destinationIs32)
        {
            // Rgba32 <-> Bgra32: red and blue are exchanged
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            for (; i + 4 <= length; i += 4)
            {
                unsafe { Vector128.Shuffle(Vector128.LoadUnsafe(ref input, (nuint)(4 * i)), order).StoreUnsafe(ref output, (nuint)(4 * i)); }
            }
        }
        else if (typeof(TSource) == typeof(Rgb24) && destinationIs32)
        {
            // 4 pixels of 3 bytes (16 bytes are read) to 4 pixels of 4 bytes
            var order = swaps
                ? Vector128.Create((byte)2, 1, 0, 0xFF, 5, 4, 3, 0xFF, 8, 7, 6, 0xFF, 11, 10, 9, 0xFF)
                : Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
            for (; i + 6 <= length; i += 4)
            {
                unsafe { (Vector128.Shuffle(Vector128.LoadUnsafe(ref input, (nuint)(3 * i)), order) | opaque).StoreUnsafe(ref output, (nuint)(4 * i)); }
            }
        }
        else if (sourceIs32 && typeof(TDestination) == typeof(Rgb24))
        {
            // 4 pixels of 4 bytes to 4 pixels of 3 bytes (16 bytes are written, the last 4 are overwritten by the next pixels)
            var order = swaps
                ? Vector128.Create((byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0xFF, 0xFF, 0xFF, 0xFF)
                : Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
            for (; i + 6 <= length; i += 4)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i)));
                if (flattens)
                    pixels = Flatten8Vector(pixels, background8);

                unsafe { Vector128.Shuffle(pixels, order).StoreUnsafe(ref output, (nuint)(3 * i)); }
            }
        }
        else if (IsGray<TSource>() && (typeof(TDestination) == typeof(Rgb24) || destinationIs32))
        {
            // 16 gray samples (reduced to 8 bits first), each three times, with an opaque alpha for 4-byte pixels
            for (; i + 16 <= length; i += 16)
            {
                var gray = typeof(TSource) == typeof(Gray8)
                    ? unsafe(Vector128.LoadUnsafe(ref input, (nuint)i))
                    : Narrow8Bit(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))).AsUInt16(), unsafe(Vector128.LoadUnsafe(ref input, (nuint)((2 * i) + 16))).AsUInt16());
                StoreGray(gray, ref output, i, rgb24: typeof(TDestination) == typeof(Rgb24));
            }
        }
        else if (typeof(TSource) == typeof(Gray16) && typeof(TDestination) == typeof(Gray8))
        {
            for (; i + 16 <= length; i += 16)
            {
                var gray = Narrow8Bit(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))).AsUInt16(), unsafe(Vector128.LoadUnsafe(ref input, (nuint)((2 * i) + 16))).AsUInt16());
                unsafe { gray.StoreUnsafe(ref output, (nuint)i); }
            }
        }
        else if (typeof(TSource) == typeof(Rgba64) && destinationIs32)
        {
            // 4 pixels of 4 samples reduced to 8 bits; only the channel order may change
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            for (; i + 4 <= length; i += 4)
            {
                var pixels = Narrow8Bit(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(8 * i))).AsUInt16(), unsafe(Vector128.LoadUnsafe(ref input, (nuint)((8 * i) + 16))).AsUInt16());
                if (swaps)
                    pixels = Vector128.Shuffle(pixels, order);

                unsafe { pixels.StoreUnsafe(ref output, (nuint)(4 * i)); }
            }
        }
        else if (typeof(TSource) == typeof(Rgba64) && typeof(TDestination) == typeof(Rgb24))
        {
            // 4 pixels reduced to 8 bits, alpha dropped (16 bytes are written, the last 4 are overwritten by the next pixels)
            var order = Vector128.Create((byte)0, 1, 2, 4, 5, 6, 8, 9, 10, 12, 13, 14, 0xFF, 0xFF, 0xFF, 0xFF);
            for (; i + 6 <= length; i += 4)
            {
                var pixels = Narrow8Bit(LoadRgba64(ref input, i, flattens, background16), LoadRgba64(ref input, i + 2, flattens, background16));
                unsafe { Vector128.Shuffle(pixels, order).StoreUnsafe(ref output, (nuint)(3 * i)); }
            }
        }
        else if (sourceIs32 && typeof(TDestination) == typeof(Rgba64))
        {
            // v * 257: the byte, twice (red and blue exchanged for Bgra32)
            var low = swaps
                ? Vector128.Create((byte)2, 2, 1, 1, 0, 0, 3, 3, 6, 6, 5, 5, 4, 4, 7, 7)
                : Vector128.Create((byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
            var high = swaps
                ? Vector128.Create((byte)10, 10, 9, 9, 8, 8, 11, 11, 14, 14, 13, 13, 12, 12, 15, 15)
                : Vector128.Create((byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
            for (; i + 4 <= length; i += 4)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i)));
                unsafe { Vector128.Shuffle(pixels, low).StoreUnsafe(ref output, (nuint)(8 * i)); }
                unsafe { Vector128.Shuffle(pixels, high).StoreUnsafe(ref output, (nuint)((8 * i) + 16)); }
            }
        }
        else if (typeof(TSource) == typeof(Rgb24) && typeof(TDestination) == typeof(Rgba64))
        {
            // 4 pixels of 3 bytes (16 bytes are read), each byte twice, and an opaque alpha
            var opaque16 = Vector128.Create(0xFFFF000000000000ul).AsByte();
            var low = Vector128.Create((byte)0, 0, 1, 1, 2, 2, 0xFF, 0xFF, 3, 3, 4, 4, 5, 5, 0xFF, 0xFF);
            var high = Vector128.Create((byte)6, 6, 7, 7, 8, 8, 0xFF, 0xFF, 9, 9, 10, 10, 11, 11, 0xFF, 0xFF);
            for (; i + 6 <= length; i += 4)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(3 * i)));
                unsafe { (Vector128.Shuffle(pixels, low) | opaque16).StoreUnsafe(ref output, (nuint)(8 * i)); }
                unsafe { (Vector128.Shuffle(pixels, high) | opaque16).StoreUnsafe(ref output, (nuint)((8 * i) + 16)); }
            }
        }
        else if (typeof(TSource) == typeof(Gray8) && Is16Bit<TDestination>())
        {
            // v * 257: each byte twice, then for Rgba64 each 16-bit sample three times and an opaque alpha
            var low = Vector128.Create((byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
            var high = Vector128.Create((byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
            for (; i + 16 <= length; i += 16)
            {
                var gray = unsafe(Vector128.LoadUnsafe(ref input, (nuint)i));
                if (typeof(TDestination) == typeof(Gray16))
                {
                    unsafe { Vector128.Shuffle(gray, low).StoreUnsafe(ref output, (nuint)(2 * i)); }
                    unsafe { Vector128.Shuffle(gray, high).StoreUnsafe(ref output, (nuint)((2 * i) + 16)); }
                }
                else
                {
                    StoreGray16AsRgba64(Vector128.Shuffle(gray, low), ref output, i);
                    StoreGray16AsRgba64(Vector128.Shuffle(gray, high), ref output, i + 8);
                }
            }
        }
        else if (typeof(TSource) == typeof(Gray16) && typeof(TDestination) == typeof(Rgba64))
        {
            for (; i + 8 <= length; i += 8)
            {
                StoreGray16AsRgba64(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(2 * i))), ref output, i);
            }
        }
        else if (!Is16Bit<TSource>() && !IsGray<TSource>() && IsGray<TDestination>())
        {
            // Luma of 16 pixels; a 16-byte load of Rgb24 pixels reads 4 bytes beyond the 4 pixels it converts
            var end = typeof(TSource) == typeof(Rgb24) ? length - 18 : length - 16;
            var low = Vector128.Create((byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
            var high = Vector128.Create((byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
            for (; i <= end; i += 16)
            {
                var y0 = Luma8Vector(LoadPixels32<TSource>(ref input, i, flattens, background8), typeof(TSource) == typeof(Bgra32)).AsUInt32();
                var y1 = Luma8Vector(LoadPixels32<TSource>(ref input, i + 4, flattens, background8), typeof(TSource) == typeof(Bgra32)).AsUInt32();
                var y2 = Luma8Vector(LoadPixels32<TSource>(ref input, i + 8, flattens, background8), typeof(TSource) == typeof(Bgra32)).AsUInt32();
                var y3 = Luma8Vector(LoadPixels32<TSource>(ref input, i + 12, flattens, background8), typeof(TSource) == typeof(Bgra32)).AsUInt32();
                var gray = Vector128.Narrow(Vector128.Narrow(y0, y1), Vector128.Narrow(y2, y3));
                if (typeof(TDestination) == typeof(Gray8))
                {
                    unsafe { gray.StoreUnsafe(ref output, (nuint)i); }
                }
                else
                {
                    // v * 257: each byte twice
                    unsafe { Vector128.Shuffle(gray, low).StoreUnsafe(ref output, (nuint)(2 * i)); }
                    unsafe { Vector128.Shuffle(gray, high).StoreUnsafe(ref output, (nuint)((2 * i) + 16)); }
                }
            }
        }
        else if (typeof(TSource) == typeof(Rgba64) && IsGray<TDestination>())
        {
            for (; i + 16 <= length; i += 16)
            {
                var y0 = Luma16Vector(LoadRgba64(ref input, i, flattens, background16), LoadRgba64(ref input, i + 2, flattens, background16));
                var y1 = Luma16Vector(LoadRgba64(ref input, i + 4, flattens, background16), LoadRgba64(ref input, i + 6, flattens, background16));
                var y2 = Luma16Vector(LoadRgba64(ref input, i + 8, flattens, background16), LoadRgba64(ref input, i + 10, flattens, background16));
                var y3 = Luma16Vector(LoadRgba64(ref input, i + 12, flattens, background16), LoadRgba64(ref input, i + 14, flattens, background16));
                var gray0 = Vector128.Narrow(y0, y1);
                var gray1 = Vector128.Narrow(y2, y3);
                if (typeof(TDestination) == typeof(Gray8))
                {
                    unsafe { Narrow8Bit(gray0, gray1).StoreUnsafe(ref output, (nuint)i); }
                }
                else
                {
                    unsafe { gray0.AsByte().StoreUnsafe(ref output, (nuint)(2 * i)); }
                    unsafe { gray1.AsByte().StoreUnsafe(ref output, (nuint)((2 * i) + 16)); }
                }
            }
        }

        return i;
    }

    /// <summary>
    /// Loads 4 pixels of an 8-bit color format as 4 lanes of 32 bits whose bytes 0, 1 and 2 are the color components in
    /// memory order, flattened onto <paramref name="background"/> when <paramref name="flattens"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> LoadPixels32<TSource>(ref byte input, int index, bool flattens, Vector128<ushort> background)
    {
        if (typeof(TSource) == typeof(Rgb24))
        {
            // 16 bytes are read for 12
            var order = Vector128.Create((byte)0, 1, 2, 0xFF, 3, 4, 5, 0xFF, 6, 7, 8, 0xFF, 9, 10, 11, 0xFF);
            return Vector128.Shuffle(unsafe(Vector128.LoadUnsafe(ref input, (nuint)(3 * index))), order);
        }

        var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * index)));
        return flattens ? Flatten8Vector(pixels, background) : pixels;
    }

    /// <summary>Loads 2 <see cref="Rgba64"/> pixels, flattened onto <paramref name="background"/> when <paramref name="flattens"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ushort> LoadRgba64(ref byte input, int index, bool flattens, Vector128<uint> background)
    {
        var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(8 * index))).AsUInt16();
        return flattens ? Flatten16Vector(pixels, background) : pixels;
    }

    /// <summary>
    /// Flattens 4 straight-alpha pixels of 8-bit samples onto an opaque background, with the result of
    /// <see cref="Flatten(uint, uint, uint, uint)"/>: <c>(s * a + bg * (255 - a) + 127) / 255</c>.
    /// </summary>
    /// <param name="pixels">4 pixels of 4 bytes, alpha last.</param>
    /// <param name="background">The background components of 2 pixels, in the order of the pixel bytes, with 0 for alpha.</param>
    /// <returns>The flattened pixels; their alpha byte is meaningless.</returns>
    /// <remarks><c>s * a + bg * (255 - a)</c> is at most 255 * 255, so it fits in 16 bits (<see cref="DivideBy255Vector"/>).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<byte> Flatten8Vector(Vector128<byte> pixels, Vector128<ushort> background)
    {
        var alpha = Vector128.Shuffle(pixels, Vector128.Create((byte)3, 3, 3, 3, 7, 7, 7, 7, 11, 11, 11, 11, 15, 15, 15, 15));
        var (lower, upper) = Vector128.Widen(pixels);
        var (lowerAlpha, upperAlpha) = Vector128.Widen(alpha);
        var max = Vector128.Create((ushort)byte.MaxValue);
        var flattenedLower = DivideBy255Vector((lower * lowerAlpha) + (background * (max - lowerAlpha)));
        var flattenedUpper = DivideBy255Vector((upper * upperAlpha) + (background * (max - upperAlpha)));
        return Vector128.Narrow(flattenedLower, flattenedUpper);
    }

    /// <summary>
    /// Computes <c>(x + 127) / 255</c> on every lane, for <c>x</c> up to 255 * 255: <c>t = x + 128</c>,
    /// <c>(t + (t &gt;&gt; 8)) &gt;&gt; 8</c> (checked exhaustively by the tests), which does not overflow 16 bits.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<ushort> DivideBy255Vector(Vector128<ushort> value)
    {
        var biased = value + Vector128.Create((ushort)128);
        return Vector128.ShiftRightLogical(biased + Vector128.ShiftRightLogical(biased, 8), 8);
    }

    /// <summary>
    /// Flattens 2 straight-alpha <see cref="Rgba64"/> pixels onto an opaque background, with the result of
    /// <see cref="Flatten(uint, uint, uint, uint)"/>: <c>(s * a + bg * (65535 - a) + 32767) / 65535</c>.
    /// </summary>
    /// <param name="pixels">2 pixels.</param>
    /// <param name="background">The background components (red, green, blue and 0 for alpha).</param>
    /// <returns>The flattened pixels; their alpha is meaningless.</returns>
    /// <remarks><c>s * a + bg * (65535 - a)</c> is at most 65535 * 65535, so it fits in 32 bits (<see cref="DivideBy65535Vector"/>).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<ushort> Flatten16Vector(Vector128<ushort> pixels, Vector128<uint> background)
    {
        var alpha = Vector128.Shuffle(pixels.AsByte(), Vector128.Create((byte)6, 7, 6, 7, 6, 7, 6, 7, 14, 15, 14, 15, 14, 15, 14, 15)).AsUInt16();
        var (first, second) = Vector128.Widen(pixels);
        var (firstAlpha, secondAlpha) = Vector128.Widen(alpha);
        var max = Vector128.Create((uint)ushort.MaxValue);
        var flattenedFirst = DivideBy65535Vector((first * firstAlpha) + (background * (max - firstAlpha)));
        var flattenedSecond = DivideBy65535Vector((second * secondAlpha) + (background * (max - secondAlpha)));
        return Vector128.Narrow(flattenedFirst, flattenedSecond);
    }

    /// <summary>
    /// Computes <c>(x + 32767) / 65535</c> on every lane, for <c>x</c> up to 65535 * 65535: <c>t = x + 32768</c>,
    /// <c>(t + (t &gt;&gt; 16)) &gt;&gt; 16</c>, which does not overflow 32 bits (at most 4,294,934,528).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> DivideBy65535Vector(Vector128<uint> value)
    {
        var biased = value + Vector128.Create(32768u);
        return Vector128.ShiftRightLogical(biased + Vector128.ShiftRightLogical(biased, 16), 16);
    }

    /// <summary>
    /// Computes the Rec. 709 luma of 4 pixels of 8-bit samples, with the result of <see cref="Luma(uint, uint, uint, uint)"/>.
    /// </summary>
    /// <param name="pixels">4 lanes of 32 bits whose bytes 0, 1 and 2 are red, green and blue (blue, green and red when <paramref name="bgr"/>).</param>
    /// <param name="bgr">Whether blue is the first byte.</param>
    /// <returns>The 4 lumas.</returns>
    /// <remarks>
    /// <c>x = 2126 R + 7152 G + 722 B + 5000</c> is at most 2,555,000, so luma never exceeds 255 and the clamp of the scalar
    /// reference has no effect. The division by 10000 is <c>(int)((float)x * (1f / 10000))</c>: <c>x</c> is exact as a
    /// <see cref="float"/>, and the result equals <c>x / 10000</c> for every <c>x</c> of that range
    /// (<see cref="DivideBy10000Vector"/>, checked exhaustively by the tests).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<int> Luma8Vector(Vector128<byte> pixels, bool bgr)
    {
        var lanes = pixels.AsInt32();
        var byteMask = Vector128.Create(0xFF);
        var first = lanes & byteMask;
        var second = Vector128.ShiftRightLogical(lanes, 8) & byteMask;
        var third = Vector128.ShiftRightLogical(lanes, 16) & byteMask;
        var (r, b) = bgr ? (third, first) : (first, third);
        var x = (r * 2126) + (second * 7152) + (b * 722) + Vector128.Create(5000);
        return DivideBy10000Vector(x);
    }

    /// <summary>Divides by 10000 every lane of a vector of integers between 0 and 2,555,000 (rounding down).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<int> DivideBy10000Vector(Vector128<int> value) => Vector128.ConvertToInt32(Vector128.ConvertToSingle(value) * Vector128.Create(1f / 10000));

    /// <summary>
    /// Computes the Rec. 709 luma of 4 <see cref="Rgba64"/> pixels, with the result of <see cref="Luma(uint, uint, uint, uint)"/>.
    /// </summary>
    /// <param name="first">The first 2 pixels.</param>
    /// <param name="second">The next 2 pixels.</param>
    /// <returns>The 4 lumas (16 bits each, in 32-bit lanes).</returns>
    /// <remarks>
    /// Narrowing the 32-bit lanes keeps red and blue (and, after a shift, green and alpha) as one 32-bit lane per pixel.
    /// <c>x = 2126 R + 7152 G + 722 B + 5000</c> is at most 655,355,000, so luma never exceeds 65535 and the clamp of the
    /// scalar reference has no effect; <see cref="DivideBy10000ExactVector"/> divides it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<uint> Luma16Vector(Vector128<ushort> first, Vector128<ushort> second)
    {
        var redBlue = Vector128.Narrow(first.AsUInt32(), second.AsUInt32()).AsInt32();
        var greenAlpha = Vector128.Narrow(Vector128.ShiftRightLogical(first.AsUInt32(), 16), Vector128.ShiftRightLogical(second.AsUInt32(), 16)).AsInt32();
        var sampleMask = Vector128.Create(0xFFFF);
        var x = ((redBlue & sampleMask) * 2126) + ((greenAlpha & sampleMask) * 7152) + (Vector128.ShiftRightLogical(redBlue, 16) * 722) + Vector128.Create(5000);
        return DivideBy10000ExactVector(x).AsUInt32();
    }

    /// <summary>Divides by 10000 every lane of a vector of integers between 0 and 655,355,000 (rounding down).</summary>
    /// <remarks>
    /// The quotient computed in <see cref="float"/> is within 0.02 of the exact quotient (three roundings of relative error
    /// 2^-24 on a quotient below 2^17), so its truncation is the exact quotient, one less or one more; the remainder
    /// corrects it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<int> DivideBy10000ExactVector(Vector128<int> value)
    {
        var quotient = DivideBy10000Vector(value);
        var remainder = value - (quotient * 10000);

        // A lane mask is all ones (-1): subtracting it adds one
        return quotient - Vector128.GreaterThanOrEqual(remainder, Vector128.Create(10000)) + Vector128.LessThan(remainder, Vector128<int>.Zero);
    }

    /// <summary>
    /// Converts 16-bit samples to 8 bits on every lane, with the result of <see cref="To8Bit(uint)"/>: <c>(v * 255 + 32767) /
    /// 65535</c> is the nearest integer to <c>v / 257</c>.
    /// </summary>
    /// <param name="value">The 16-bit samples.</param>
    /// <returns>The 8-bit samples, one per 16-bit lane.</returns>
    /// <remarks>
    /// With <c>v = 256 h + l</c>, <c>v = 257 h + (l - h)</c>, so <c>v / 257</c> rounds to <c>h</c> plus the rounding of
    /// <c>(l - h) / 257</c>, where <c>-255 &lt;= l - h &lt;= 255</c>: one more when <c>l - h &gt; 128</c>, one less when
    /// <c>l - h &lt; -128</c> (no ties: 257 is odd).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector128<ushort> To8BitVector(Vector128<ushort> value)
    {
        var high = Vector128.ShiftRightLogical(value, 8);
        var difference = (value & Vector128.Create((ushort)0xFF)).AsInt16() - high.AsInt16();
        var up = Vector128.GreaterThan(difference, Vector128.Create((short)128)).AsUInt16();
        var down = Vector128.LessThan(difference, Vector128.Create((short)-128)).AsUInt16();

        // A lane mask is all ones (-1): subtracting it adds one
        return high - up + down;
    }

    /// <summary>Converts sixteen 16-bit samples to 8 bits (<see cref="To8Bit(uint)"/>) and packs them in order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> Narrow8Bit(Vector128<ushort> lower, Vector128<ushort> upper) => Vector128.Narrow(To8BitVector(lower), To8BitVector(upper));

    /// <summary>Stores 16 gray samples as 16 <see cref="Rgb24"/> pixels (48 bytes) or as 16 opaque 4-byte pixels (64 bytes, the same bytes for both channel orders).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreGray(Vector128<byte> gray, ref byte output, int index, bool rgb24)
    {
        if (rgb24)
        {
            var first = Vector128.Create((byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5);
            var second = Vector128.Create((byte)5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10);
            var third = Vector128.Create((byte)10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15);
            unsafe { Vector128.Shuffle(gray, first).StoreUnsafe(ref output, (nuint)(3 * index)); }
            unsafe { Vector128.Shuffle(gray, second).StoreUnsafe(ref output, (nuint)((3 * index) + 16)); }
            unsafe { Vector128.Shuffle(gray, third).StoreUnsafe(ref output, (nuint)((3 * index) + 32)); }
        }
        else
        {
            var opaque = Vector128.Create(0xFF000000u).AsByte();
            var first = Vector128.Create((byte)0, 0, 0, 0xFF, 1, 1, 1, 0xFF, 2, 2, 2, 0xFF, 3, 3, 3, 0xFF);
            var second = Vector128.Create((byte)4, 4, 4, 0xFF, 5, 5, 5, 0xFF, 6, 6, 6, 0xFF, 7, 7, 7, 0xFF);
            var third = Vector128.Create((byte)8, 8, 8, 0xFF, 9, 9, 9, 0xFF, 10, 10, 10, 0xFF, 11, 11, 11, 0xFF);
            var fourth = Vector128.Create((byte)12, 12, 12, 0xFF, 13, 13, 13, 0xFF, 14, 14, 14, 0xFF, 15, 15, 15, 0xFF);
            unsafe { (Vector128.Shuffle(gray, first) | opaque).StoreUnsafe(ref output, (nuint)(4 * index)); }
            unsafe { (Vector128.Shuffle(gray, second) | opaque).StoreUnsafe(ref output, (nuint)((4 * index) + 16)); }
            unsafe { (Vector128.Shuffle(gray, third) | opaque).StoreUnsafe(ref output, (nuint)((4 * index) + 32)); }
            unsafe { (Vector128.Shuffle(gray, fourth) | opaque).StoreUnsafe(ref output, (nuint)((4 * index) + 48)); }
        }
    }

    /// <summary>Stores 8 gray 16-bit samples as 8 opaque <see cref="Rgba64"/> pixels (64 bytes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreGray16AsRgba64(Vector128<byte> gray, ref byte output, int index)
    {
        var opaque16 = Vector128.Create(0xFFFF000000000000ul).AsByte();
        var first = Vector128.Create((byte)0, 1, 0, 1, 0, 1, 0xFF, 0xFF, 2, 3, 2, 3, 2, 3, 0xFF, 0xFF);
        var second = Vector128.Create((byte)4, 5, 4, 5, 4, 5, 0xFF, 0xFF, 6, 7, 6, 7, 6, 7, 0xFF, 0xFF);
        var third = Vector128.Create((byte)8, 9, 8, 9, 8, 9, 0xFF, 0xFF, 10, 11, 10, 11, 10, 11, 0xFF, 0xFF);
        var fourth = Vector128.Create((byte)12, 13, 12, 13, 12, 13, 0xFF, 0xFF, 14, 15, 14, 15, 14, 15, 0xFF, 0xFF);
        unsafe { (Vector128.Shuffle(gray, first) | opaque16).StoreUnsafe(ref output, (nuint)(8 * index)); }
        unsafe { (Vector128.Shuffle(gray, second) | opaque16).StoreUnsafe(ref output, (nuint)((8 * index) + 16)); }
        unsafe { (Vector128.Shuffle(gray, third) | opaque16).StoreUnsafe(ref output, (nuint)((8 * index) + 32)); }
        unsafe { (Vector128.Shuffle(gray, fourth) | opaque16).StoreUnsafe(ref output, (nuint)((8 * index) + 48)); }
    }

    private static void ConvertCore<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination, Rgba64? background)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        // All the "typeof" tests below are JIT-time constants for each (value type) instantiation
        var sourceMax = Is16Bit<TSource>() ? Max16 : Max8;
        uint bgR = 0, bgG = 0, bgB = 0;
        var flatten = background is not null;
        if (background is { } bg)
        {
            if (Is16Bit<TSource>())
            {
                (bgR, bgG, bgB) = (bg.R, bg.G, bg.B);
            }
            else
            {
                (bgR, bgG, bgB) = (To8Bit(bg.R), To8Bit(bg.G), To8Bit(bg.B));
            }
        }

        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));
        for (nint i = 0; i < source.Length; i++)
        {
            Read(ref unsafe(Unsafe.Add(ref src, i)), out var r, out var g, out var b, out var a);
            if (flatten && a != sourceMax)
            {
                r = Flatten(r, bgR, a, sourceMax);
                g = Flatten(g, bgG, a, sourceMax);
                b = Flatten(b, bgB, a, sourceMax);
                a = sourceMax;
            }

            Write<TSource, TDestination>(ref unsafe(Unsafe.Add(ref dst, i)), r, g, b, a, sourceMax);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Is16Bit<TPixel>() => typeof(TPixel) == typeof(Rgba64) || typeof(TPixel) == typeof(Gray16);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGray<TPixel>() => typeof(TPixel) == typeof(Gray8) || typeof(TPixel) == typeof(Gray16);

    /// <summary>Reads the components at the source precision; gray is replicated and missing alpha is opaque.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Read<TPixel>(ref TPixel pixel, out uint r, out uint g, out uint b, out uint a)
    {
        if (typeof(TPixel) == typeof(Rgba32))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Rgba32>(ref pixel));
            (r, g, b, a) = (p.R, p.G, p.B, p.A);
        }
        else if (typeof(TPixel) == typeof(Bgra32))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Bgra32>(ref pixel));
            (r, g, b, a) = (p.R, p.G, p.B, p.A);
        }
        else if (typeof(TPixel) == typeof(Rgb24))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Rgb24>(ref pixel));
            (r, g, b, a) = (p.R, p.G, p.B, Max8);
        }
        else if (typeof(TPixel) == typeof(Rgba64))
        {
            ref var p = ref unsafe(Unsafe.As<TPixel, Rgba64>(ref pixel));
            (r, g, b, a) = (p.R, p.G, p.B, p.A);
        }
        else if (typeof(TPixel) == typeof(Gray8))
        {
            uint v = unsafe(Unsafe.As<TPixel, Gray8>(ref pixel)).Value;
            (r, g, b, a) = (v, v, v, Max8);
        }
        else if (typeof(TPixel) == typeof(Gray16))
        {
            uint v = unsafe(Unsafe.As<TPixel, Gray16>(ref pixel)).Value;
            (r, g, b, a) = (v, v, v, Max16);
        }
        else
        {
            throw new UnreachableException();
        }
    }

    /// <summary>Writes components given at the source precision: luma (color to gray), then the precision change.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Write<TSource, TDestination>(ref TDestination pixel, uint r, uint g, uint b, uint a, uint sourceMax)
    {
        if (IsGray<TDestination>())
        {
            var y = IsGray<TSource>() ? r : Luma(r, g, b, sourceMax);
            if (typeof(TDestination) == typeof(Gray8))
            {
                unsafe { Unsafe.As<TDestination, Gray8>(ref pixel).Value = Is16Bit<TSource>() ? To8Bit(y) : (byte)y; }
            }
            else
            {
                unsafe { Unsafe.As<TDestination, Gray16>(ref pixel).Value = Is16Bit<TSource>() ? (ushort)y : To16Bit(y); }
            }
        }
        else if (Is16Bit<TDestination>())
        {
            ref var p = ref unsafe(Unsafe.As<TDestination, Rgba64>(ref pixel));
            if (Is16Bit<TSource>())
            {
                (p.R, p.G, p.B, p.A) = ((ushort)r, (ushort)g, (ushort)b, (ushort)a);
            }
            else
            {
                (p.R, p.G, p.B, p.A) = (To16Bit(r), To16Bit(g), To16Bit(b), To16Bit(a));
            }
        }
        else
        {
            byte r8, g8, b8, a8;
            if (Is16Bit<TSource>())
            {
                (r8, g8, b8, a8) = (To8Bit(r), To8Bit(g), To8Bit(b), To8Bit(a));
            }
            else
            {
                (r8, g8, b8, a8) = ((byte)r, (byte)g, (byte)b, (byte)a);
            }

            if (typeof(TDestination) == typeof(Rgba32))
            {
                ref var p = ref unsafe(Unsafe.As<TDestination, Rgba32>(ref pixel));
                (p.R, p.G, p.B, p.A) = (r8, g8, b8, a8);
            }
            else if (typeof(TDestination) == typeof(Bgra32))
            {
                ref var p = ref unsafe(Unsafe.As<TDestination, Bgra32>(ref pixel));
                (p.B, p.G, p.R, p.A) = (b8, g8, r8, a8);
            }
            else
            {
                ref var p = ref unsafe(Unsafe.As<TDestination, Rgb24>(ref pixel));
                (p.R, p.G, p.B) = (r8, g8, b8);
            }
        }
    }
}
