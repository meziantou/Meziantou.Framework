using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// A text writer that behaves as a <see cref="StringWriter"/>, whose buffer is rented from <see cref="ArrayPool{T}.Shared"/>:
/// the serializers write the whole markup before reading it, so only the resulting string is allocated (the chunks of a
/// <see cref="StringBuilder"/> add up to the length of the result). The buffer is returned by <see cref="Release"/>, not by
/// <see cref="TextWriter.Dispose()"/>: as for a <see cref="StringWriter"/>, a disposed writer cannot be written, but its
/// content can still be read (a custom <see cref="HtmlMarkupWriter"/> may dispose its output).
/// </summary>
internal sealed class PooledStringWriter : TextWriter
{
    private const int InitialCapacity = 256;

    private static readonly UnicodeEncoding Utf16Encoding = new(bigEndian: false, byteOrderMark: false);

    private char[] _buffer = ArrayPool<char>.Shared.Rent(InitialCapacity);
    private int _length;
    private bool _disposed;

    public PooledStringWriter()
        : base(CultureInfo.InvariantCulture)
    {
    }

    public override Encoding Encoding => Utf16Encoding;

    public override void Write(char value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = value;
    }

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(index, count));
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.Length > _buffer.Length - _length)
        {
            Grow(buffer.Length);
        }

        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
    }

    public override void Write(string? value)
    {
        if (value is not null)
        {
            Write(value.AsSpan());
        }
    }

    public override string ToString() => new(_buffer, 0, _length);

    /// <summary>Returns the buffer to the pool: the writer is no longer usable.</summary>
    public void Release()
    {
        _disposed = true;
        var buffer = _buffer;
        _buffer = [];
        _length = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }

    private void Grow(int additionalCapacity)
    {
        var newBuffer = ArrayPool<char>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + additionalCapacity));
        _buffer.AsSpan(0, _length).CopyTo(newBuffer);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
