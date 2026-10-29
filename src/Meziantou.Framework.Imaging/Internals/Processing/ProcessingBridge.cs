using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Runs the filtering kernels (resize, transform, convolution, blurs, median) on the pixel formats they do not handle
/// themselves: <see cref="La16"/>, <see cref="A8"/>, <see cref="Argb32"/> and <see cref="Abgr32"/> are processed as
/// <see cref="Rgba32"/>, <see cref="Bgr565"/> as <see cref="Rgb24"/>, and <see cref="La32"/> and <see cref="Rgba1010102"/>
/// as <see cref="Rgba64"/>, in a temporary copy of the frame charged to the scope of the image.
/// </summary>
/// <remarks>
/// <para>
/// The copy of a gray format has the same precision and the same alpha, and its three colors are the gray level (black for
/// <see cref="A8"/>). The kernels compute the three colors with the same arithmetic, so they stay equal, and the copy
/// converts back exactly (the luma of a gray color is that gray): the result is the one of a gray kernel that treats alpha
/// like the other formats do. A kernel that makes the colors differ (an operation that colors pixels) gives their luma
/// back, and <see cref="A8"/> keeps the alpha only.
/// </para>
/// <para>
/// The copy of <see cref="Argb32"/> and <see cref="Abgr32"/> has the same samples in another order: the result is the one
/// of <see cref="Rgba32"/>. The copy of a packed format has the nearest samples of its components, and the result is
/// rounded back to the nearest components: the one of the wider format, stored in the packed one.
/// </para>
/// </remarks>
internal static class ProcessingBridge
{
    /// <summary>An operation on the leased working copy of a frame, in place.</summary>
    public delegate void InPlaceKernel(scoped in PixelLease working);

    /// <summary>An operation that reads the working copy of a frame and writes the working copy of its result.</summary>
    public delegate void CopyKernel(scoped in PixelLease source, scoped in PixelLease destination);

    /// <summary>Gets a value indicating whether the kernels process <typeparamref name="TPixel"/> through a working copy.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBridged<TPixel>() => typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(La32) || typeof(TPixel) == typeof(A8) || typeof(TPixel) == typeof(Argb32) || typeof(TPixel) == typeof(Abgr32) || typeof(TPixel) == typeof(Bgr565) || typeof(TPixel) == typeof(Rgba1010102);

    /// <summary>Gets a value indicating whether the working copy of <typeparamref name="TPixel"/> has three equal colors: a gray format, or <see cref="A8"/> (black).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasGrayColors<TPixel>() => typeof(TPixel) == typeof(La16) || typeof(TPixel) == typeof(La32) || typeof(TPixel) == typeof(A8);

    /// <summary>Gets the pixel format the kernels work in for a pixel format.</summary>
    public static PixelFormat GetKernelFormat(PixelFormat format) => format switch
    {
        PixelFormat.La16 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 => PixelFormat.Rgba32,
        PixelFormat.Bgr565 => PixelFormat.Rgb24,
        PixelFormat.La32 or PixelFormat.Rgba1010102 => PixelFormat.Rgba64,
        _ => format,
    };

    /// <summary>Copies a leased frame to a working copy, runs <paramref name="kernel"/> on it, and copies the result back.</summary>
    public static void RunInPlace<TPixel>(StorageOwner owner, scoped in PixelLease lease, InPlaceKernel kernel)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var working = GetKernelFormat(format);
        var storage = owner.Allocate(lease.Width, lease.Height, PixelFormats.GetBytesPerPixel(working), options: null, AllocationKind.Temporary);
        try
        {
            using var workingLease = storage.AcquireLease();
            CopyRows(lease, format, workingLease, working);
            kernel(workingLease);
            CopyRows(workingLease, working, lease, format);
        }
        finally
        {
            storage.Dispose();
        }
    }

    /// <summary>Copies a leased source to a working copy, runs <paramref name="kernel"/> into a working destination, and copies it to <paramref name="destination"/>.</summary>
    public static void RunCopy<TPixel>(StorageOwner owner, scoped in PixelLease source, scoped in PixelLease destination, CopyKernel kernel)
        where TPixel : unmanaged
    {
        var format = PixelFormats.GetPixelFormat<TPixel>();
        var working = GetKernelFormat(format);
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(working);
        var sourceStorage = owner.Allocate(source.Width, source.Height, bytesPerPixel, options: null, AllocationKind.Temporary);
        PixelStorage? destinationStorage = null;
        try
        {
            destinationStorage = owner.Allocate(destination.Width, destination.Height, bytesPerPixel, options: null, AllocationKind.Temporary);
            using var sourceLease = sourceStorage.AcquireLease();
            using var destinationLease = destinationStorage.AcquireLease();
            CopyRows(source, format, sourceLease, working);
            kernel(sourceLease, destinationLease);
            CopyRows(destinationLease, working, destination, format);
        }
        finally
        {
            destinationStorage?.Dispose();
            sourceStorage.Dispose();
        }
    }

    private static void CopyRows(scoped in PixelLease source, PixelFormat sourceFormat, scoped in PixelLease destination, PixelFormat destinationFormat)
    {
        var sourceLength = source.Width * PixelFormats.GetBytesPerPixel(sourceFormat);
        for (var y = 0; y < source.Height; y++)
        {
            _ = PixelConverter.ConvertRow(sourceFormat, source.GetRowBytes(y)[..sourceLength], destinationFormat, destination.GetRowBytes(y));
        }
    }
}
