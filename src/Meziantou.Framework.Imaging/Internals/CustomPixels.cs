using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The checks of the custom pixel types (<see cref="ICustomPixel{TSelf}"/>).</summary>
internal static class CustomPixels
{
    /// <summary>Gets the storage format of a custom pixel type, which must be a built-in pixel format.</summary>
    /// <exception cref="NotSupportedException">The storage format is not a built-in pixel format.</exception>
    public static PixelFormat GetStorageFormat<TPixel>()
        where TPixel : unmanaged, ICustomPixel<TPixel>
    {
        var format = TPixel.StorageFormat;
        if (format == PixelFormat.Unknown || !Enum.IsDefined(format))
            throw new NotSupportedException($"The custom pixel type '{typeof(TPixel).FullName}' declares the storage format '{format}', which is not a built-in pixel format.");

        return format;
    }

    /// <summary>Rents a row of <paramref name="width"/> pixels of <paramref name="bytesPerPixel"/> bytes, charged to <paramref name="scope"/>.</summary>
    /// <exception cref="ImageResourceLimitException">The row exceeds the allocation limit.</exception>
    public static PooledBuffer RentRow(AllocationScope scope, int width, int bytesPerPixel)
    {
        if ((long)width * bytesPerPixel > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        return scope.Rent(width * bytesPerPixel, AllocationKind.Temporary, clear: true);
    }

    /// <summary>Gets the size of a custom pixel, in bytes.</summary>
    public static int GetSize<TPixel>()
        where TPixel : unmanaged
        => Unsafe.SizeOf<TPixel>();
}
