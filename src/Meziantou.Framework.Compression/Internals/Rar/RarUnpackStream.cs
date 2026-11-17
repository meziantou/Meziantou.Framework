using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The decoded content of a compressed entry. At the end of the content, the decoder finishes the file so that the next file of a solid stream can follow.</summary>
internal sealed class RarUnpackStream : ReadOnlyStreamBase
{
    private readonly RarUnpacker _unpacker;
    private readonly Stream _packed;
    private bool _disposed;

    public RarUnpackStream(RarUnpacker unpacker, Stream packed)
    {
        _unpacker = unpacker;
        _packed = packed;
    }

    /// <summary>Gets a value indicating whether the content was read to its end.</summary>
    public bool IsCompleted { get; private set; }

    public RarUnpacker Unpacker => _unpacker;

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty || IsCompleted)
            return 0;

        var read = _unpacker.Read(buffer);
        if (read is 0)
        {
            _unpacker.FinishFile();
            IsCompleted = true;
        }

        return read;
    }

    /// <summary>Reads the rest of the content and discards it.</summary>
    public void Drain()
    {
        var buffer = new byte[81920];
        while (Read(buffer) > 0)
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _packed.Dispose();
        }

        base.Dispose(disposing);
    }
}
