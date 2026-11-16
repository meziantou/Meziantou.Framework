using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The compressed output of a compression stream: the encoders write to memory, and the compression stream copies the
/// bytes to the base stream after each write, with a synchronous or an asynchronous write.
/// </summary>
internal sealed class PendingWriteStream : WriteOnlyStreamBase
{
    private byte[] _buffer = new byte[1 << 12];
    private int _length;

    /// <summary>Gets the number of bytes written to the stream since its creation.</summary>
    public long TotalWritten { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_length + buffer.Length > _buffer.Length)
        {
            Array.Resize(ref _buffer, (int)Math.Min(Array.MaxLength, Math.Max((long)_buffer.Length * 2, (long)_length + buffer.Length)));
        }

        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
        TotalWritten += buffer.Length;
    }

    /// <summary>Gets the number of bytes not copied yet.</summary>
    public int PendingLength => _length;

    /// <summary>Replaces pending bytes, from an offset in the pending bytes.</summary>
    public void Overwrite(int offset, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, _length - data.Length);
        data.CopyTo(_buffer.AsSpan(offset));
    }

    /// <summary>Writes the pending bytes to <paramref name="stream"/>.</summary>
    public void CopyPendingTo(Stream stream)
    {
        if (_length > 0)
        {
            stream.Write(_buffer, 0, _length);
            _length = 0;
        }
    }

    /// <summary>Writes the pending bytes to <paramref name="stream"/>.</summary>
    public async ValueTask CopyPendingToAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (_length > 0)
        {
            await stream.WriteAsync(_buffer.AsMemory(0, _length), cancellationToken).ConfigureAwait(false);
            _length = 0;
        }
    }
}
