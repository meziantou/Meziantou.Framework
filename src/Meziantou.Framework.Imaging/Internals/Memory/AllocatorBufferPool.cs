namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The buffers of the scopes of a configuration that has an <see cref="ImageConfiguration.BufferAllocator"/>: every
/// rental and return goes to the allocator, and the length it declares (<see cref="ImageBufferAllocator.GetBufferLength"/>)
/// is what the scope charges before renting. An array of another length is returned to the allocator and rejected, so
/// that the charged bytes are always the bytes in use.
/// </summary>
internal sealed class AllocatorBufferPool(ImageBufferAllocator allocator) : IBufferPool
{
    public ImageBufferAllocator Allocator { get; } = allocator;

    public long GetRentCapacity(long length)
    {
        if (length <= 0)
            return 0;

        // A request no array can hold is charged as it is: the charge fails first, or the rental does
        if (length > Array.MaxLength)
            return length;

        var capacity = Allocator.GetBufferLength((int)length);
        if (capacity < length || capacity > Array.MaxLength)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The buffer allocator '{Allocator.GetType().FullName}' declares a buffer of {capacity} bytes for a request of {length} bytes: GetBufferLength must return a length from the requested length to Array.MaxLength."));

        return capacity;
    }

    public byte[] Rent(int length, bool clear)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Array.MaxLength);
        if (length == 0)
            return [];

        var capacity = GetRentCapacity(length);
        var buffer = Allocator.Rent(length);
        if (buffer is null || buffer.Length != capacity)
        {
            if (buffer is { Length: > 0 })
            {
                Allocator.Return(buffer);
            }

            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The buffer allocator '{Allocator.GetType().FullName}' returned {(buffer is null ? "null" : $"an array of {buffer.Length} bytes")} for a request of {length} bytes, but declared {capacity} bytes (GetBufferLength)."));
        }

        if (clear)
        {
            buffer.AsSpan(0, length).Clear();
        }

        return buffer;
    }

    public bool Return(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0)
            return false;

        Allocator.Return(buffer);
        return true;
    }
}
