using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

public abstract partial class Image
{
    /// <summary>
    /// Creates a single-frame image over pixel memory the caller owns, without copying it: reading and writing the pixels of
    /// the image reads and writes the memory.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the memory, laid out like <typeparamref name="TPixel"/> (16-bit components in native endianness).</typeparam>
    /// <param name="memory">The pixels, row by row, top to bottom.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInBytes">
    /// The distance between the starts of two consecutive rows, in bytes, or 0 for tightly packed rows. Must be 0 or at least
    /// <c>width * bytesPerPixel</c>; it does not need to be a multiple of the pixel size.
    /// </param>
    /// <param name="release">
    /// The callback invoked once the image no longer uses the memory, such as the <see cref="IDisposable.Dispose"/> method of
    /// the <see cref="System.Buffers.IMemoryOwner{T}"/> of <paramref name="memory"/>, or <see langword="null"/>. It is not invoked when this method throws.
    /// </param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <remarks>
    /// <para>
    /// The operations that edit pixels in place (row callbacks, drawing, filters, color adjustments, flips) write to the
    /// memory. The operations that change the geometry (crop, resize, rotation, transforms) move the frame to memory of the
    /// library and release the wrapped memory, as does <see cref="Dispose"/>; frames appended later are memory of the
    /// library too. The memory is not charged to <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>, and its content
    /// is used as is.
    /// </para>
    /// <para>
    /// The caller must not use the memory while an operation of the image does, and must keep it valid until
    /// <paramref name="release"/> is invoked. Dispose the image to release the memory: the image has no finalizer. An
    /// exception thrown by <paramref name="release"/> is thrown by the operation that released the memory.
    /// </para>
    /// <para>
    /// With a 16-bit or floating-point pixel type, a memory or a stride that is not aligned on the size of a component (2 or
    /// 4 bytes) makes the operations read and write the components at unaligned addresses: this works, but it can be slower.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="memory"/> is too short for the dimensions and stride (the last row only needs <c>width * bytesPerPixel</c> bytes).</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static Image<TPixel> WrapMemory<TPixel>(Memory<byte> memory, int width, int height, int strideInBytes = 0, Action? release = null, ImageConfiguration? configuration = null)
        where TPixel : unmanaged
    {
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        var stride = ValidateWrappedMemory(pixelFormat, memory.Length, width, height, strideInBytes, nameof(memory), ref configuration);
        return new Image<TPixel>(configuration, new Size(width, height), memory, pointer: 0, stride, release);
    }

    /// <summary>
    /// Creates a single-frame image of a runtime pixel format over pixel memory the caller owns, without copying it (see
    /// <see cref="WrapMemory{TPixel}(Memory{byte}, int, int, int, Action?, ImageConfiguration?)"/>).
    /// </summary>
    /// <param name="pixelFormat">The pixel format of the memory.</param>
    /// <param name="memory">The pixels, row by row, top to bottom.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInBytes">The distance between the starts of two consecutive rows, in bytes, or 0 for tightly packed rows.</param>
    /// <param name="release">The callback invoked once the image no longer uses the memory, or <see langword="null"/>.</param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller, an <see cref="Image{TPixel}"/> of the pixel type of <paramref name="pixelFormat"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelFormat"/> is not defined, or a dimension or the stride is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="memory"/> is too short for the dimensions and stride.</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static Image WrapMemory(PixelFormat pixelFormat, Memory<byte> memory, int width, int height, int strideInBytes = 0, Action? release = null, ImageConfiguration? configuration = null)
    {
        var stride = ValidateWrappedMemory(pixelFormat, memory.Length, width, height, strideInBytes, nameof(memory), ref configuration);
        return CreateWrapped(pixelFormat, configuration, new Size(width, height), memory, pointer: 0, stride, release);
    }

    /// <summary>
    /// Creates a single-frame image over native pixel memory the caller owns, without copying it (see
    /// <see cref="WrapMemory{TPixel}(Memory{byte}, int, int, int, Action?, ImageConfiguration?)"/>): a camera frame, a
    /// bitmap of another library, a GPU staging buffer.
    /// </summary>
    /// <typeparam name="TPixel">The pixel type of the memory.</typeparam>
    /// <param name="address">The address of the first pixel of the first row.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInBytes">
    /// The distance between the starts of two consecutive rows, in bytes, or 0 for tightly packed rows. Must be 0 or at least
    /// <c>width * bytesPerPixel</c>.
    /// </param>
    /// <param name="release">The callback invoked once the image no longer uses the memory (to free it or to unlock the buffer), or <see langword="null"/>.</param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <remarks>
    /// The memory from <paramref name="address"/> must hold <c>(height - 1) * stride + width * bytesPerPixel</c> readable and
    /// writable bytes, which cannot be checked, until <paramref name="release"/> is invoked.
    /// </remarks>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension or the stride is invalid.</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static unsafe Image<TPixel> WrapMemory<TPixel>(void* address, int width, int height, int strideInBytes = 0, Action? release = null, ImageConfiguration? configuration = null)
        where TPixel : unmanaged
    {
        var pixelFormat = PixelFormats.GetPixelFormat<TPixel>();
        if (address is null)
            throw new ArgumentNullException(nameof(address));
        var stride = ValidateWrappedMemory(pixelFormat, bufferLength: null, width, height, strideInBytes, nameof(address), ref configuration);
        return new Image<TPixel>(configuration, new Size(width, height), Memory<byte>.Empty, (nint)address, stride, release);
    }

    /// <summary>
    /// Creates a single-frame image of a runtime pixel format over native pixel memory the caller owns, without copying it
    /// (see <see cref="WrapMemory{TPixel}(void*, int, int, int, Action?, ImageConfiguration?)"/>).
    /// </summary>
    /// <param name="pixelFormat">The pixel format of the memory.</param>
    /// <param name="address">The address of the first pixel of the first row.</param>
    /// <param name="width">The width of the image. Must be positive.</param>
    /// <param name="height">The height of the image. Must be positive.</param>
    /// <param name="strideInBytes">The distance between the starts of two consecutive rows, in bytes, or 0 for tightly packed rows.</param>
    /// <param name="release">The callback invoked once the image no longer uses the memory, or <see langword="null"/>.</param>
    /// <param name="configuration">The configuration of the new image, or <see langword="null"/> for <see cref="ImageConfiguration.Default"/>.</param>
    /// <returns>A new image owned by the caller, an <see cref="Image{TPixel}"/> of the pixel type of <paramref name="pixelFormat"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pixelFormat"/> is not defined, or a dimension or the stride is invalid.</exception>
    /// <exception cref="ImageResourceLimitException">The image would exceed a configured limit.</exception>
    public static unsafe Image WrapMemory(PixelFormat pixelFormat, void* address, int width, int height, int strideInBytes = 0, Action? release = null, ImageConfiguration? configuration = null)
    {
        if (address is null)
            throw new ArgumentNullException(nameof(address));
        var stride = ValidateWrappedMemory(pixelFormat, bufferLength: null, width, height, strideInBytes, nameof(address), ref configuration);
        return CreateWrapped(pixelFormat, configuration, new Size(width, height), Memory<byte>.Empty, (nint)address, stride, release);
    }

    /// <summary>Validates the arguments of a wrap, in the order of the copied imports, and resolves the stride and the configuration.</summary>
    private static long ValidateWrappedMemory(PixelFormat pixelFormat, int? bufferLength, int width, int height, int strideInBytes, string bufferName, [NotNull] ref ImageConfiguration? configuration)
    {
        if (!Enum.IsDefined(pixelFormat))
            throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "The pixel format is not valid.");

        var bytesPerPixel = PixelFormats.GetBytesPerPixel(pixelFormat);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegative(strideInBytes);
        var rowLength = (long)width * bytesPerPixel;
        var stride = RowStride.Resolve(strideInBytes, rowLength, nameof(strideInBytes), "bytes");
        if (bufferLength is { } length)
        {
            RowStride.EnsureLength(length, stride, rowLength, height, bufferName, "bytes");
        }
        else if (!CheckedSizes.TryMultiply(stride, height - 1L, out var offset) || !CheckedSizes.TryAdd(offset, rowLength, out _))
        {
            throw new ArgumentOutOfRangeException(nameof(strideInBytes), strideInBytes, "The rows do not fit in the address space.");
        }

        configuration ??= ImageConfiguration.Default;
        configuration.Limits.EnsureCanvasWithinLimits(width, height);
        return stride;
    }

    private static Image CreateWrapped(PixelFormat format, ImageConfiguration configuration, Size size, Memory<byte> memory, nint pointer, long stride, Action? release) => format switch
    {
        PixelFormat.Rgba32 => new Image<Rgba32>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Bgra32 => new Image<Bgra32>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Rgb24 => new Image<Rgb24>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Rgba64 => new Image<Rgba64>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Gray8 => new Image<Gray8>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Gray16 => new Image<Gray16>(configuration, size, memory, pointer, stride, release),
        PixelFormat.RgbaFloat => new Image<RgbaFloat>(configuration, size, memory, pointer, stride, release),
        PixelFormat.RgbaHalf => new Image<RgbaHalf>(configuration, size, memory, pointer, stride, release),
        PixelFormat.La16 => new Image<La16>(configuration, size, memory, pointer, stride, release),
        PixelFormat.La32 => new Image<La32>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Rgb48 => new Image<Rgb48>(configuration, size, memory, pointer, stride, release),
        PixelFormat.A8 => new Image<A8>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Argb32 => new Image<Argb32>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Abgr32 => new Image<Abgr32>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Bgr565 => new Image<Bgr565>(configuration, size, memory, pointer, stride, release),
        PixelFormat.Rgba1010102 => new Image<Rgba1010102>(configuration, size, memory, pointer, stride, release),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The pixel format is not supported."),
    };
}
