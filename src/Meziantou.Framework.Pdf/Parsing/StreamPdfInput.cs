namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// A file read on demand from a seekable stream, from the position the stream had when the document was opened. The
/// stream belongs to the caller: it must stay open and unchanged while the document is used.
/// </summary>
internal sealed class StreamPdfInput : PdfInput
{
    private readonly Stream _stream;
    private readonly long _origin;
    private readonly long _length;

    public StreamPdfInput(Stream stream)
    {
        _stream = stream;
        _origin = stream.Position;
        _length = Math.Max(0, stream.Length - _origin);
    }

    public override long Length => _length;

    public override bool IsInMemory => false;

    public override int Read(long offset, Span<byte> destination)
    {
        if (offset >= _length)
            return 0;

        if (destination.Length > _length - offset)
        {
            destination = destination[..(int)(_length - offset)];
        }

        _stream.Position = _origin + offset;
        var total = 0;
        while (total < destination.Length)
        {
            var read = _stream.Read(destination[total..]);
            if (read <= 0)
                break;

            total += read;
        }

        return total;
    }
}
