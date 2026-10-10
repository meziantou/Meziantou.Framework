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
    public static IReadOnlyList<PixelFormat> All { get; } = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

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
            throw new NotSupportedException($"Pixel type '{typeof(TPixel).FullName}' is not supported. Supported pixel types are Rgba32, Bgra32, Rgb24, Rgba64, Gray8 and Gray16.");

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
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of bytes used to store one pixel.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The number of bytes per pixel.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetBytesPerPixel(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 => 4,
        PixelFormat.Rgb24 => 3,
        PixelFormat.Rgba64 => 8,
        PixelFormat.Gray8 => 1,
        PixelFormat.Gray16 => 2,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of bits used to store each component.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>8 or 16.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetBitsPerComponent(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Gray8 => 8,
        PixelFormat.Rgba64 or PixelFormat.Gray16 => 16,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Gets the number of components (channels) of a pixel, including alpha.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The number of components.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static int GetComponentCount(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64 => 4,
        PixelFormat.Rgb24 => 3,
        PixelFormat.Gray8 or PixelFormat.Gray16 => 1,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format stores an alpha component.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if the format has a (straight) alpha component; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool HasAlpha(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgba64 => true,
        PixelFormat.Rgb24 or PixelFormat.Gray8 or PixelFormat.Gray16 => false,
        _ => throw InvalidFormat(format),
    };

    /// <summary>Determines whether the pixel format is grayscale.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns><see langword="true"/> if the format stores a single gray component; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported pixel format.</exception>
    public static bool IsGrayscale(PixelFormat format) => format switch
    {
        PixelFormat.Gray8 or PixelFormat.Gray16 => true,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24 or PixelFormat.Rgba64 => false,
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

        return PixelFormat.Unknown;
    }

    private static ArgumentOutOfRangeException InvalidFormat(PixelFormat format)
        => new(nameof(format), format, "The pixel format is not supported.");
}
