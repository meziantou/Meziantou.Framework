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
/// vectors, the conversions that only move bytes (channel order, alpha added or dropped, gray replicated, 8-bit samples
/// to 16 bits) and the search for a non-opaque pixel process 16 bytes at a time.
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

        // The conversions that only move bytes are done 16 bytes at a time; flattening is arithmetic
        var flattens = removesAlpha && background is not null;
        var done = useVectors && !flattens ? ConvertVector(source, destination) : 0;
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
    /// Converts the leading pixels of a row for the pairs of formats whose conversion only moves bytes (channel order, added
    /// opaque alpha, dropped alpha, replicated gray, 8-bit samples repeated into 16 bits), 16 bytes at a time.
    /// </summary>
    /// <returns>The number of pixels converted; the scalar reference converts the others.</returns>
    /// <remarks>
    /// Alpha is only dropped once the caller has checked that it is opaque (or that discarding it is the conversion). A
    /// vector that holds 12 meaningful bytes is read or written as 16 bytes while 16 bytes remain in the row.
    /// </remarks>
    private static int ConvertVector<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
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
                unsafe { Vector128.Shuffle(Vector128.LoadUnsafe(ref input, (nuint)(4 * i)), order).StoreUnsafe(ref output, (nuint)(3 * i)); }
            }
        }
        else if (typeof(TSource) == typeof(Gray8) && typeof(TDestination) == typeof(Rgb24))
        {
            // 16 gray samples, each three times
            var first = Vector128.Create((byte)0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5);
            var second = Vector128.Create((byte)5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10);
            var third = Vector128.Create((byte)10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15);
            for (; i + 16 <= length; i += 16)
            {
                var gray = unsafe(Vector128.LoadUnsafe(ref input, (nuint)i));
                unsafe { Vector128.Shuffle(gray, first).StoreUnsafe(ref output, (nuint)(3 * i)); }
                unsafe { Vector128.Shuffle(gray, second).StoreUnsafe(ref output, (nuint)((3 * i) + 16)); }
                unsafe { Vector128.Shuffle(gray, third).StoreUnsafe(ref output, (nuint)((3 * i) + 32)); }
            }
        }
        else if (typeof(TSource) == typeof(Gray8) && destinationIs32)
        {
            // 16 gray samples, each three times and an opaque alpha (the same bytes for both channel orders)
            var first = Vector128.Create((byte)0, 0, 0, 0xFF, 1, 1, 1, 0xFF, 2, 2, 2, 0xFF, 3, 3, 3, 0xFF);
            var second = Vector128.Create((byte)4, 4, 4, 0xFF, 5, 5, 5, 0xFF, 6, 6, 6, 0xFF, 7, 7, 7, 0xFF);
            var third = Vector128.Create((byte)8, 8, 8, 0xFF, 9, 9, 9, 0xFF, 10, 10, 10, 0xFF, 11, 11, 11, 0xFF);
            var fourth = Vector128.Create((byte)12, 12, 12, 0xFF, 13, 13, 13, 0xFF, 14, 14, 14, 0xFF, 15, 15, 15, 0xFF);
            for (; i + 16 <= length; i += 16)
            {
                var gray = unsafe(Vector128.LoadUnsafe(ref input, (nuint)i));
                unsafe { (Vector128.Shuffle(gray, first) | opaque).StoreUnsafe(ref output, (nuint)(4 * i)); }
                unsafe { (Vector128.Shuffle(gray, second) | opaque).StoreUnsafe(ref output, (nuint)((4 * i) + 16)); }
                unsafe { (Vector128.Shuffle(gray, third) | opaque).StoreUnsafe(ref output, (nuint)((4 * i) + 32)); }
                unsafe { (Vector128.Shuffle(gray, fourth) | opaque).StoreUnsafe(ref output, (nuint)((4 * i) + 48)); }
            }
        }
        else if (typeof(TSource) == typeof(Rgba32) && typeof(TDestination) == typeof(Rgba64))
        {
            // v * 257: the byte, twice
            var low = Vector128.Create((byte)0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7);
            var high = Vector128.Create((byte)8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15);
            for (; i + 4 <= length; i += 4)
            {
                var pixels = unsafe(Vector128.LoadUnsafe(ref input, (nuint)(4 * i)));
                unsafe { Vector128.Shuffle(pixels, low).StoreUnsafe(ref output, (nuint)(8 * i)); }
                unsafe { Vector128.Shuffle(pixels, high).StoreUnsafe(ref output, (nuint)((8 * i) + 16)); }
            }
        }

        return i;
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
