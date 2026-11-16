using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.SevenZip;

/// <summary>Counts the bytes written to a stream.</summary>
internal sealed class CountingWriteStream : WriteOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Disposed unless leaveOpen")]
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private bool _disposed;

    public CountingWriteStream(Stream inner, bool leaveOpen)
    {
        _inner = inner;
        _leaveOpen = leaveOpen;
    }

    public long Count { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _inner.Write(buffer);
        Count += buffer.Length;
    }

    public override void Flush() => _inner.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            if (_leaveOpen)
            {
                _inner.Flush();
            }
            else
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
