namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The source of the buffers an <see cref="AllocationScope"/> charges: the process-wide <see cref="SlabPool"/>, or the
/// <see cref="ImageBufferAllocator"/> of a configuration (<see cref="AllocatorBufferPool"/>). The capacity of a rental is
/// known before the buffer is rented, so that the scope charges it first.
/// </summary>
internal interface IBufferPool
{
    /// <summary>Gets the capacity of the buffer that <see cref="Rent(int, bool)"/> returns for a length, which is the charged size.</summary>
    /// <param name="length">The requested length (non-negative; may exceed the maximum buffer length).</param>
    long GetRentCapacity(long length);

    /// <summary>Rents a buffer of at least <paramref name="length"/> bytes; its length is <see cref="GetRentCapacity(long)"/>.</summary>
    /// <param name="length">The requested length, from 0 to <see cref="Array.MaxLength"/>.</param>
    /// <param name="clear"><see langword="true"/> to zero the first <paramref name="length"/> bytes.</param>
    byte[] Rent(int length, bool clear);

    /// <summary>Returns a buffer obtained from <see cref="Rent(int, bool)"/>. The caller must not use it afterward.</summary>
    /// <returns><see langword="true"/> if the buffer is retained for reuse.</returns>
    bool Return(byte[] buffer);
}
