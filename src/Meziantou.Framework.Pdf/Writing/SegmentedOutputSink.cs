namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Keeps the bytes as a list of blocks, written to a stream asynchronously once the document is serialized, or copied to
/// one array: the output never needs one growing array, whose copies cost up to three times the size of the file.
/// </summary>
internal sealed class SegmentedOutputSink : PdfOutputSink
{
    private readonly List<ReadOnlyMemory<byte>> _segments = [];

    public long Length { get; private set; }

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

    public byte[] ToArray()
    {
        if (Length > Array.MaxLength)
            throw new PdfResourceLimitException("The output exceeds the maximum size of an array");

        var result = new byte[Length];
        var offset = 0;
        foreach (var segment in _segments)
        {
            segment.Span.CopyTo(result.AsSpan(offset));
            offset += segment.Length;
        }

        return result;
    }

    public async Task WriteToAsync(Stream stream, CancellationToken cancellationToken)
    {
        foreach (var segment in _segments)
        {
            await stream.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Add(ReadOnlyMemory<byte> segment)
    {
        if (segment.Length == 0)
            return;

        _segments.Add(segment);
        Length += segment.Length;
    }
}
