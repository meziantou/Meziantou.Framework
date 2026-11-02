namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A file in memory.</summary>
internal sealed class MemoryPdfInput(ReadOnlyMemory<byte> data) : PdfInput
{
    public ReadOnlyMemory<byte> Data => data;

    public override long Length => data.Length;

    public override bool IsInMemory => true;

    public override int Read(long offset, Span<byte> destination)
    {
        if (offset >= data.Length)
            return 0;

        var source = data.Span[(int)offset..];
        var count = Math.Min(source.Length, destination.Length);
        source[..count].CopyTo(destination);
        return count;
    }

    public override ReadOnlyMemory<byte> GetMemory(long offset, int length) => data.Slice((int)offset, length);
}
