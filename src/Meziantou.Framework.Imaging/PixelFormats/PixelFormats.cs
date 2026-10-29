namespace Meziantou.Framework.Imaging;

/// <summary>Provides information about the built-in pixel formats and maps pixel structs to <see cref="PixelFormat"/> values.</summary>
/// <remarks>
/// Typed APIs constrain their pixel type parameter with <c>where TPixel : unmanaged</c> only. Unmanaged types that are
/// not one of the built-in pixel structs are rejected with a <see cref="NotSupportedException"/> before any I/O or
/// allocation happens. There is no public pixel-implementation contract in this version.
/// </remarks>
public static class PixelFormats
{
    /// <summary>Gets the pixel formats supported by this version, in declaration order.</summary>
    public static IReadOnlyList<PixelFormat> All { get; } = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.RgbaFloat, PixelFormat.RgbaHalf, PixelFormat.La16, PixelFormat.La32, PixelFormat.Rgb48, PixelFormat.A8, PixelFormat.Argb32, PixelFormat.Abgr32, PixelFormat.Bgr565, PixelFormat.Rgba1010102];

    /// <summary>Determines whether <typeparamref name="TPixel"/> is one of the built-in pixel structs.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <returns><see langword="true"/> if the type is supported; otherwise <see langword="false"/>.</returns>
    public static bool IsSupported<TPixel>()
        where TPixel : unmanaged
        => TryGetPixelFormat<TPixel>() != PixelFormat.Unknown;

    /// <summary>Gets the <see cref="PixelFormat"/> corresponding to <typeparamref name="TPixel"/>.</summary>
    /// <typeparam name="TPixel">The pixel type.</typeparam>
    /// <returns>The pixel format.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    public static PixelFormat GetPixelFormat<TPixel>()
        where TPixel : unmanaged
    {
        var format = TryGetPixelFormat<TPixel>();
        if (format == PixelFormat.Unknown)
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).FullName}' is not supported. Supported pixel types are Rgba32, Bgra32, Rgb24, Rgba64, Gray8, Gray16, RgbaFloat, RgbaHalf, La16, La32, Rgb48, A8, Argb32, Abgr32, Bgr565 and Rgba1010102.");

        return format;
    }

    /// <summary>Gets the CLR pixel struct type corresponding to a pixel format.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The pixel struct type.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static Type GetPixelType(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 => typeof(Rgba32),
        PixelFormat.Bgra32 => typeof(Bgra32),
        PixelFormat.Rgb24 => typeof(Rgb24),
        PixelFormat.Rgba64 => typeof(Rgba64),
        PixelFormat.Gray8 => typeof(Gray8),
        PixelFormat.Gray16 => typeof(Gray16),
        PixelFormat.RgbaFloat => typeof(RgbaFloat),
        PixelFormat.RgbaHalf => typeof(RgbaHalf),
        PixelFormat.La16 => typeof(La16),
        PixelFormat.La32 => typeof(La32),
        PixelFormat.Rgb48 => typeof(Rgb48),
        PixelFormat.A8 => typeof(A8),
        PixelFormat.Argb32 => typeof(Argb32),
        PixelFormat.Abgr32 => typeof(Abgr32),
        PixelFormat.Bgr565 => typeof(Bgr565),
        PixelFormat.Rgba1010102 => typeof(Rgba1010102),
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of bytes used to store one pixel.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The number of bytes per pixel.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetBytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.La32 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Rgba1010102 => 4,
        PixelFormat.Rgb24 => 3,
        PixelFormat.Rgba64 or PixelFormat.RgbaHalf => 8,
        PixelFormat.Gray8 or PixelFormat.A8 => 1,
        PixelFormat.Gray16 or PixelFormat.La16 or PixelFormat.Bgr565 => 2,
        PixelFormat.Rgb48 => 6,
        PixelFormat.RgbaFloat => 16,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of bits used to store each component.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>
    /// 8 or 16 for the integer formats; 32 for <see cref="PixelFormat.RgbaFloat"/> and 16 for <see cref="PixelFormat.RgbaHalf"/>
    /// (see <see cref="IsFloatingPoint"/>). The packed formats, whose components do not all have the same size, give the bits
    /// of their largest component: 6 for <see cref="PixelFormat.Bgr565"/> (5-bit red and blue, 6-bit green) and 10 for
    /// <see cref="PixelFormat.Rgba1010102"/> (10-bit colors, 2-bit alpha).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetBitsPerComponent(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Gray8 or PixelFormat.La16 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 => 8,
        PixelFormat.Rgba64 or PixelFormat.Gray16 or PixelFormat.RgbaHalf or PixelFormat.La32 or PixelFormat.Rgb48 => 16,
        PixelFormat.RgbaFloat => 32,
        PixelFormat.Bgr565 => 6,
        PixelFormat.Rgba1010102 => 10,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of components (channels) of a pixel, including alpha.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The number of components.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetComponentCount(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64 or PixelFormat.RgbaFloat or PixelFormat.RgbaHalf or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Rgba1010102 => 4,
        PixelFormat.Rgb24 or PixelFormat.Rgb48 or PixelFormat.Bgr565 => 3,
        PixelFormat.La16 or PixelFormat.La32 => 2,
        PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.A8 => 1,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format stores an alpha component.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if the format has a (straight) alpha component; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool HasAlpha(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64 or PixelFormat.RgbaFloat or PixelFormat.RgbaHalf or PixelFormat.La16 or PixelFormat.La32 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Rgba1010102 => true,
        PixelFormat.Rgb24 or PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.Rgb48 or PixelFormat.Bgr565 => false,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format is grayscale.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>
    /// <see langword="true"/> if the format stores a single gray component, with or without alpha (<see cref="PixelFormat.Gray8"/>,
    /// <see cref="PixelFormat.Gray16"/>, <see cref="PixelFormat.La16"/> and <see cref="PixelFormat.La32"/>); otherwise
    /// <see langword="false"/>, also for <see cref="PixelFormat.A8"/>, which stores no color (see <see cref="HasColor"/>).
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsGrayscale(PixelFormat format) => format switch
    {
        PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.La16 or PixelFormat.La32 => true,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 or PixelFormat.RgbaFloat or PixelFormat.RgbaHalf or PixelFormat.Rgb48 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Bgr565 or PixelFormat.Rgba1010102 => false,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format stores floating-point components.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>
    /// <see langword="true"/> for <see cref="PixelFormat.RgbaFloat"/> and <see cref="PixelFormat.RgbaHalf"/>, whose
    /// components are not limited to [0, 1]; <see langword="false"/> for the integer formats, whose components go from 0
    /// to the largest value of their bits.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsFloatingPoint(PixelFormat format) => format switch
    {
        PixelFormat.RgbaFloat or PixelFormat.RgbaHalf => true,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 or PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Bgr565 or PixelFormat.Rgba1010102 => false,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format stores a color (gray or red, green and blue components).</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>
    /// <see langword="false"/> for <see cref="PixelFormat.A8"/>, an alpha mask whose pixels read as black; otherwise
    /// <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool HasColor(PixelFormat format) => format switch
    {
        PixelFormat.A8 => false,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 or PixelFormat.Gray8 or PixelFormat.Gray16 or PixelFormat.RgbaFloat or PixelFormat.RgbaHalf or PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Bgr565 or PixelFormat.Rgba1010102 => true,
        _ => throw InvalidFormat(format),
    };

    internal static PixelFormat TryGetPixelFormat<TPixel>()
        where TPixel : unmanaged
    {
        // These checks are JIT-time constants for each instantiation
        if (typeof(TPixel) == typeof(Rgba32))
            return PixelFormat.Rgba32;

        if (typeof(TPixel) == typeof(Bgra32))
            return PixelFormat.Bgra32;

        if (typeof(TPixel) == typeof(Rgb24))
            return PixelFormat.Rgb24;

        if (typeof(TPixel) == typeof(Rgba64))
            return PixelFormat.Rgba64;

        if (typeof(TPixel) == typeof(Gray8))
            return PixelFormat.Gray8;

        if (typeof(TPixel) == typeof(Gray16))
            return PixelFormat.Gray16;

        if (typeof(TPixel) == typeof(RgbaFloat))
            return PixelFormat.RgbaFloat;

        if (typeof(TPixel) == typeof(RgbaHalf))
            return PixelFormat.RgbaHalf;

        if (typeof(TPixel) == typeof(La16))
            return PixelFormat.La16;

        if (typeof(TPixel) == typeof(La32))
            return PixelFormat.La32;

        if (typeof(TPixel) == typeof(Rgb48))
            return PixelFormat.Rgb48;

        if (typeof(TPixel) == typeof(A8))
            return PixelFormat.A8;

        if (typeof(TPixel) == typeof(Argb32))
            return PixelFormat.Argb32;

        if (typeof(TPixel) == typeof(Abgr32))
            return PixelFormat.Abgr32;

        if (typeof(TPixel) == typeof(Bgr565))
            return PixelFormat.Bgr565;

        if (typeof(TPixel) == typeof(Rgba1010102))
            return PixelFormat.Rgba1010102;

        return PixelFormat.Unknown;
    }

    private static ArgumentOutOfRangeException InvalidFormat(PixelFormat format)
        => new(nameof(format), format, "The pixel format is not supported.");
}
