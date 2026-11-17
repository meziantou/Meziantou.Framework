using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// A buffered forward-only stream whose reader can give back the bytes it read too far (a decoder that reads past the end of
/// its data, a look-ahead): they are read again by the next reads. <see cref="Position"/> counts the bytes consumed.
/// </summary>
internal sealed class PushbackStream : ReadOnlyStreamBase
{
    private const int BufferLength = 64 * 1024;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The owner of the stream disposes the input")]
    private readonly Stream _input;
    private byte[] _buffer = new byte[BufferLength];
    private int _offset;
    private int _count;
    private long _position;

    public PushbackStream(Stream input)
    {
        _input = input;
    }

    /// <summary>Gets the number of bytes consumed: the bytes read minus the bytes given back.</summary>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;

        if (_count is 0)
        {
            // A large read bypasses the buffer
            if (buffer.Length >= BufferLength)
            {
                var direct = _input.Read(buffer);
                _position += direct;
                return direct;
            }

            _offset = 0;
            _count = _input.Read(_buffer);
            if (_count is 0)
                return 0;
        }

        var length = Math.Min(_count, buffer.Length);
        _buffer.AsSpan(_offset, length).CopyTo(buffer);
        _offset += length;
        _count -= length;
        _position += length;
        return length;
    }

    /// <summary>Gives back bytes: the next reads return them first.</summary>
    public void Unread(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        if (data.Length <= _offset)
        {
            _offset -= data.Length;
        }
        else
        {
            var buffer = new byte[Math.Max(BufferLength, data.Length + _count)];
            _buffer.AsSpan(_offset, _count).CopyTo(buffer.AsSpan(data.Length));
            _buffer = buffer;
            _offset = 0;
        }

        data.CopyTo(_buffer.AsSpan(_offset));
        _count += data.Length;
        _position -= data.Length;
    }

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes, fewer only at the end of the stream.</summary>
    public int ReadAtMost(Span<byte> buffer) => this.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
}
