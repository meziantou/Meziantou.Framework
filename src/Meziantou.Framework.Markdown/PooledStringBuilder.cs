using System.Buffers;
using System.Globalization;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// Builds a string in a buffer rented from <see cref="ArrayPool{T}.Shared"/>, which <see cref="Dispose"/> returns: unlike
/// <see cref="System.Text.StringBuilder"/>, writing a document does not allocate the chunks that hold the text, only the
/// string at the end.
/// </summary>
internal sealed class PooledStringBuilder : IDisposable
{
    private char[] _buffer;
    private int _length;

    public PooledStringBuilder(int capacity)
    {
        _buffer = ArrayPool<char>.Shared.Rent(Math.Max(capacity, 16));
    }

    public int Length => _length;

    /// <summary>Gets the last character, or <c>\0</c> when the text is empty.</summary>
    public char LastChar => _length > 0 ? _buffer[_length - 1] : '\0';

    public PooledStringBuilder Append(char value)
    {
        if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = value;
        return this;
    }

    public PooledStringBuilder Append(char value, int repeatCount)
    {
        if (repeatCount > 0)
        {
            EnsureCapacity(repeatCount);
            _buffer.AsSpan(_length, repeatCount).Fill(value);
            _length += repeatCount;
        }

        return this;
    }

    public PooledStringBuilder Append(string? value) => Append(value.AsSpan());

    public PooledStringBuilder Append(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
        return this;
    }

    public PooledStringBuilder Append(int value)
    {
        EnsureCapacity(11);
        _ = value.TryFormat(_buffer.AsSpan(_length), out var written, provider: CultureInfo.InvariantCulture);
        _length += written;
        return this;
    }

    public void Insert(int index, ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        _buffer.AsSpan(index, _length - index).CopyTo(_buffer.AsSpan(index + value.Length));
        value.CopyTo(_buffer.AsSpan(index));
        _length += value.Length;
    }

    public ReadOnlySpan<char> AsSpan() => _buffer.AsSpan(0, _length);

    public void Clear() => _length = 0;

    public override string ToString() => new(_buffer, 0, _length);

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _length = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private void EnsureCapacity(int count)
    {
        if (_buffer.Length - _length < count)
        {
            Grow(count);
        }
    }

    private void Grow(int count)
    {
        var buffer = ArrayPool<char>.Shared.Rent(Math.Max(_length + count, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(buffer);
        var previous = _buffer;
        _buffer = buffer;
        if (previous.Length > 0)
        {
            ArrayPool<char>.Shared.Return(previous);
        }
    }
}
