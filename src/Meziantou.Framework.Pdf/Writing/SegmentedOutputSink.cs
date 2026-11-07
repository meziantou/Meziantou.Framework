using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Keeps the bytes as a list of blocks, written to a stream asynchronously once the document is serialized, or copied to
/// one array: the output never needs one growing array, whose copies cost up to three times the size of the file. The
/// ranges of the file the document was read from (the original file of an incremental update, the data of the streams
/// not read yet) are kept as ranges and read when they are written, so that the output can be larger than the memory.
/// </summary>
internal sealed class SegmentedOutputSink : PdfOutputSink
{
    private readonly List<Segment> _segments = [];

    public long Length { get; private set; }

    public override bool KeepsInputs => true;

    public override byte[] Flush(byte[] buffer, int count)
    {
        // A buffer flushed before a large block is mostly empty: its bytes are copied, and the buffer is reused
        if (count < buffer.Length / 2)
        {
            Add(buffer.AsSpan(0, count).ToArray());
            return buffer;
        }

        Add(buffer.AsMemory(0, count));
        return new byte[buffer.Length];
    }

    public override void Write(ReadOnlySpan<byte> data) => Add(data.ToArray());

    public override void Write(ReadOnlyMemory<byte> data, bool isImmutable) => Add(isImmutable ? data : data.ToArray());

    public override void Write(PdfInput input, long offset, long length)
    {
        if (length == 0)
            return;

        _segments.Add(new Segment(default, input, offset, length));
        Length += length;
    }

    public byte[] ToArray()
    {
        if (Length > Array.MaxLength)
            throw new PdfResourceLimitException("The output exceeds the maximum size of an array");

        var result = new byte[Length];
        var offset = 0;
        foreach (var segment in _segments)
        {
            if (segment.Input is { } input)
            {
                PdfByteWriter.ForEachChunk(input, segment.Offset, segment.Length, (buffer, start, count) =>
                {
                    buffer.AsSpan(start, count).CopyTo(result.AsSpan(offset));
                    offset += count;
                });
            }
            else
            {
                segment.Data.Span.CopyTo(result.AsSpan(offset));
                offset += segment.Data.Length;
            }
        }

        return result;
    }

    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[]? buffer = null;
        foreach (var segment in _segments)
        {
            if (segment.Input is { } input)
            {
                // The input is read synchronously (the document reads its stream so), and written asynchronously
                buffer ??= new byte[1024 * 1024];
                for (long offset = 0; offset < segment.Length;)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = input.Read(segment.Offset + offset, buffer.AsSpan(0, (int)Math.Min(buffer.Length, segment.Length - offset)));
                    if (read <= 0)
                        throw new IOException("The stream the document was read from ended before the data to write: it changed after the document was opened");

                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    offset += read;
                }
            }
            else
            {
                await stream.WriteAsync(segment.Data, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void Add(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
            return;

        _segments.Add(new Segment(data, null, 0, data.Length));
        Length += data.Length;
    }

    /// <summary>A block of memory, or a range of an input.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(ReadOnlyMemory<byte> Data, PdfInput? Input, long Offset, long Length);
}
