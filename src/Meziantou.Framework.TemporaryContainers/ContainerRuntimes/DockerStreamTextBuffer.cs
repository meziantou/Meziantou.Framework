using System.Buffers;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Decodes the payloads of one stream of a multiplexed response and splits them into lines.</summary>
/// <remarks>
/// The daemon cuts frames wherever its reads end, not between characters or lines, so the decoder keeps its state from
/// one frame to the next, and the decoded characters stay in a pooled buffer until a newline ends them. The part of the
/// buffer already searched for a newline is not searched again, and the buffer is compacted only once a line has been
/// read out of it, so a long line delivered in many frames costs a time linear in its length.
/// </remarks>
internal sealed class DockerStreamTextBuffer : IDisposable
{
    private const int InitialCapacity = 1024;

    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private char[] _chars = ArrayPool<char>.Shared.Rent(InitialCapacity);
    private int _start;
    private int _scanned;
    private int _length;

    /// <summary>Gets the characters decoded and not read yet.</summary>
    public ReadOnlySpan<char> Pending => _chars.AsSpan(_start, _length - _start);

    /// <summary>Decodes a payload, after the characters already buffered.</summary>
    /// <param name="bytes">The payload.</param>
    /// <param name="flush">Whether the stream ended, so a character whose bytes are incomplete is decoded as a replacement character.</param>
    public void Append(ReadOnlySpan<byte> bytes, bool flush)
    {
        // The maximum accounts for the bytes the decoder kept from the previous payload.
        EnsureCapacity(Encoding.UTF8.GetMaxCharCount(bytes.Length));
        _length += _decoder.GetChars(bytes, _chars.AsSpan(_length), flush);
    }

    /// <summary>Reads the next line terminated by a newline, without the newline and the carriage returns before it.</summary>
    public bool TryReadLine(out ReadOnlySpan<char> line)
    {
        var index = _chars.AsSpan(_scanned, _length - _scanned).IndexOf('\n');
        if (index < 0)
        {
            _scanned = _length;
            line = default;
            return false;
        }

        var end = _scanned + index;
        line = _chars.AsSpan(_start, end - _start).TrimEnd('\r');
        _start = end + 1;
        _scanned = _start;
        return true;
    }

    /// <summary>Discards the characters not read yet.</summary>
    public void Clear()
    {
        _start = 0;
        _scanned = 0;
        _length = 0;
    }

    private void EnsureCapacity(int additionalCapacity)
    {
        // Moving the pending characters to the start happens only after a line was read: a line that is still incomplete
        // stays where it is, so appending to it never copies it.
        if (_start > 0)
        {
            var pending = _length - _start;
            _chars.AsSpan(_start, pending).CopyTo(_chars);
            _scanned -= _start;
            _length = pending;
            _start = 0;
        }

        var required = _length + additionalCapacity;
        if (required <= _chars.Length)
            return;

        var chars = ArrayPool<char>.Shared.Rent(Math.Max(required, _chars.Length * 2));
        _chars.AsSpan(0, _length).CopyTo(chars);
        ArrayPool<char>.Shared.Return(_chars);
        _chars = chars;
    }

    public void Dispose()
    {
        var chars = _chars;
        _chars = [];
        Clear();
        if (chars.Length > 0)
            ArrayPool<char>.Shared.Return(chars);
    }
}
