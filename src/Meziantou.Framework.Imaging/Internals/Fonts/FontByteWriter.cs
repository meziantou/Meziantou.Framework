namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A growing sequence of bytes in storage rented from an allocation scope: the tables a font container reconstructs
/// before the size of the font is known.
/// </summary>
internal sealed class FontByteWriter : IDisposable
{
    private const int InitialCapacity = 4096;

    private readonly FontAllocator _scope;
    private FontBuffer? _buffer;

    public FontByteWriter(FontAllocator scope)
    {
        _scope = scope;
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the bytes written.</summary>
    public ReadOnlySpan<byte> Written => _buffer is null ? default : _buffer.Span[..Length];

    /// <summary>
    /// Adds bytes, whose content is unspecified, and returns them. The span is valid until the next call of this method.
    /// </summary>
    /// <exception cref="FontResourceLimitException">The bytes exceed the allocation limit.</exception>
    public Span<byte> Append(long count)
    {
        var required = Length + count;
        if (_buffer is null || required > _buffer.Length)
        {
            Grow(required);
        }

        var result = _buffer!.Span.Slice(Length, (int)count);
        Length = (int)required;
        return result;
    }

    /// <summary>Removes the last bytes: the part of the last addition that was not needed.</summary>
    public void Truncate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, Length);
        Length = length;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        Length = 0;
    }

    private void Grow(long required)
    {
        if (required > Array.MaxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.LiveAllocationBytes, _scope.Limit, required);

        var capacity = Math.Min(Array.MaxLength, Math.Max(required, Math.Max(InitialCapacity, (long)(_buffer?.Length ?? 0) * 2)));
        var buffer = _scope.Rent((int)capacity, clear: false);
        if (_buffer is not null)
        {
            _buffer.Span[..Length].CopyTo(buffer.Span);
            _buffer.Dispose();
        }

        _buffer = buffer;
    }
}
