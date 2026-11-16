using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>Decodes .lzma data: the header is read by the first read.</summary>
internal sealed class LzmaAloneReader : ReadOnlyStreamBase
{
    private readonly LzmaInputBuffer _input;
    private LzmaDecoderStream? _decoder;
    private bool _disposed;
    private readonly long _maxMemoryUsage;

    public LzmaAloneReader(Stream input, bool leaveOpen, long maxMemoryUsage = long.MaxValue)
    {
        _input = new LzmaInputBuffer(input, leaveOpen);
        _maxMemoryUsage = maxMemoryUsage;
    }

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer.IsEmpty)
            return 0;

        _decoder ??= LzmaAloneFormat.CreateDecoder(_input, _maxMemoryUsage);
        return _decoder.Read(buffer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _decoder?.Dispose();
            _input.Dispose();
        }

        base.Dispose(disposing);
    }
}
