using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Compression.Internals.IO;

/// <summary>A read-only view of a stream that does not dispose it.</summary>
internal sealed class NonClosingStream : ReadOnlyStreamBase
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The view does not own the stream")]
    private readonly Stream _inner;
    private bool _disposed;

    public NonClosingStream(Stream inner)
    {
        _inner = inner;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _inner.Read(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _inner.ReadAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
