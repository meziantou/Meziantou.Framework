namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes the bytes to a stream as they come, through the bounded buffer of the writer.</summary>
internal sealed class StreamOutputSink(Stream stream) : PdfOutputSink
{
    public override byte[] Flush(byte[] buffer, int count)
    {
        stream.Write(buffer, 0, count);
        return buffer;
    }

    public override void Write(ReadOnlySpan<byte> data) => stream.Write(data);
}
