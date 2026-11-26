using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The characters of an SVG document being written, in a buffer of the array pool: it grows when the document is a string,
/// and is written to a <see cref="TextWriter"/> when it is full. Unlike the ones of a <see cref="TextWriter"/>, its methods
/// are not virtual, and the document does not go through the chunks of a <see cref="System.Text.StringBuilder"/>.
/// </summary>
internal sealed class SvgOutput : IDisposable
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The writer belongs to the caller of SvgSceneRenderer.Render")]
    private readonly TextWriter? _writer;
    private char[] _buffer;
    private int _length;

    /// <summary>Initializes an output whose characters <see cref="ToString"/> returns.</summary>
    /// <param name="initialCapacity">The number of characters the buffer has at first.</param>
    public SvgOutput(int initialCapacity)
    {
        _buffer = ArrayPool<char>.Shared.Rent(initialCapacity);
    }

    /// <summary>Initializes an output that writes its characters to a writer, by blocks of a few thousand characters.</summary>
    /// <param name="writer">The destination. <see cref="Flush"/> writes the last characters.</param>
    public SvgOutput(TextWriter writer)
        : this(4096)
    {
        _writer = writer;
    }

    public void Write(char value)
    {
        var buffer = _buffer;
        var length = _length;
        if ((uint)length < (uint)buffer.Length)
        {
            buffer[length] = value;
            _length = length + 1;
        }
        else
        {
            GetSpan(1)[0] = value;
            _length++;
        }
    }

    public void Write(string? value) => Write(value.AsSpan());

    public void Write(ReadOnlySpan<char> value)
    {
        if (value.TryCopyTo(_buffer.AsSpan(_length)))
        {
            _length += value.Length;
            return;
        }

        WriteSlow(value);
    }

    /// <summary>Returns the free space of the buffer, of <paramref name="sizeHint"/> characters at least; <see cref="Advance"/> commits the characters written to it.</summary>
    public Span<char> GetSpan(int sizeHint)
    {
        if (_buffer.Length - _length < sizeHint)
        {
            MakeRoom(sizeHint);
        }

        return _buffer.AsSpan(_length);
    }

    public void Advance(int count) => _length += count;

    /// <summary>Writes the characters of the buffer to the writer, if any.</summary>
    public void Flush()
    {
        if (_writer is null || _length is 0)
            return;

        // The buffer is empty before the writer is called: when it throws, a later flush does not write the characters again
        var length = _length;
        _length = 0;
        _writer.Write(_buffer, 0, length);
    }

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

    private void WriteSlow(ReadOnlySpan<char> value)
    {
        // A writer receives a long value in pieces of the size of the buffer
        while (_writer is not null && value.Length > _buffer.Length - _length)
        {
            var count = _buffer.Length - _length;
            value[..count].CopyTo(_buffer.AsSpan(_length));
            _length += count;
            value = value[count..];
            Flush();
        }

        value.CopyTo(GetSpan(value.Length));
        _length += value.Length;
    }

    private void MakeRoom(int sizeHint)
    {
        Flush();
        if (_buffer.Length - _length >= sizeHint)
            return;

        var buffer = ArrayPool<char>.Shared.Rent(Math.Max(_length + sizeHint, (int)Math.Min(Array.MaxLength, 2L * _buffer.Length)));
        _buffer.AsSpan(0, _length).CopyTo(buffer);
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = buffer;
    }
}
