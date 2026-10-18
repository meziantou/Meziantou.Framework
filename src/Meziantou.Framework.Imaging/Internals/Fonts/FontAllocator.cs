using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The storage of the temporary buffers of an operation of the font code, bounded by a number of live bytes.</summary>
internal abstract class FontAllocator
{
    /// <summary>Gets the maximum number of bytes that can be rented at the same time.</summary>
    public abstract long Limit { get; }

    /// <summary>Rents a buffer.</summary>
    /// <param name="length">The length of the buffer, in bytes.</param>
    /// <param name="clear"><see langword="true"/> to set the bytes to zero; otherwise their content is unspecified.</param>
    /// <exception cref="FontResourceLimitException">The buffers would exceed <see cref="Limit"/>.</exception>
    public abstract FontBuffer Rent(int length, bool clear);

    /// <summary>Rents a buffer of elements, set to zero.</summary>
    /// <exception cref="FontResourceLimitException">The buffer cannot be allocated, or the buffers would exceed <see cref="Limit"/>.</exception>
    public FontBuffer Rent<T>(long count)
        where T : unmanaged
    {
        var bytes = Math.Max(count, 1) * Unsafe.SizeOf<T>();
        if (count > Array.MaxLength || bytes > Array.MaxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.LiveAllocationBytes, Limit, count > Array.MaxLength ? null : bytes);

        return Rent((int)bytes, clear: true);
    }
}
