namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>A range of bytes of the input of a file: the data of a stream that is not read yet.</summary>
/// <param name="Input">The input.</param>
/// <param name="Offset">The offset of the first byte in the input.</param>
/// <param name="Length">The number of bytes.</param>
internal sealed record PdfInputRange(PdfInput Input, long Offset, int Length)
{
    public ReadOnlyMemory<byte> Read() => Input.GetMemory(Offset, Length);
}
