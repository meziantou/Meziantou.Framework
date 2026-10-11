using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An array of <typeparamref name="T"/> rented from the decoder's allocation scope (<see cref="AllocationKind.DecoderState"/>):
/// the typed view of a <see cref="PooledBuffer"/>. Every buffer of the AV1 decoder is rented through this type, so that it is
/// charged to the scope before it is rented.
/// </summary>
internal sealed class Av1PooledArray<T> : IDisposable
    where T : unmanaged
{
    private PooledBuffer? _buffer;

    private Av1PooledArray(PooledBuffer buffer, int length)
    {
        _buffer = buffer;
        Length = length;
    }

    /// <summary>Gets the number of elements.</summary>
    public int Length { get; }

    /// <summary>Gets the elements.</summary>
    /// <exception cref="ObjectDisposedException">The array is disposed.</exception>
    public Span<T> Span
    {
        get
        {
            var buffer = _buffer ?? throw new ObjectDisposedException(nameof(Av1PooledArray<>));
            return unsafe(MemoryMarshal.Cast<byte, T>(buffer.RawBuffer.AsSpan()))[..Length];
        }
    }

    /// <summary>Rents an array of <paramref name="length"/> elements.</summary>
    /// <exception cref="ImageResourceLimitException">The array exceeds the live-allocation limit or the maximum buffer length.</exception>
    public static Av1PooledArray<T> Rent(AllocationScope scope, long length, bool clear = true, AllocationKind kind = AllocationKind.DecoderState)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var bytes = length * Marshal.SizeOf<T>();
        if (bytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        return new Av1PooledArray<T>(scope.Rent((int)bytes, kind, clear), (int)length);
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
