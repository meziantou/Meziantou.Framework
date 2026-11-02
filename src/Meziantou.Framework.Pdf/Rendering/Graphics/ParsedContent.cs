using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// The operations of a content stream, read once to be run many times (the glyph procedures of a Type 3 font): the
/// operations up to the end, or up to the one that cannot be read, and the error that stopped the reading.
/// </summary>
internal sealed class ParsedContent
{
    private ParsedContent(int length, List<(string Name, PdfObject[] Operands)> operations, Exception? error)
    {
        Length = length;
        Operations = operations;
        Error = error;
    }

    /// <summary>Gets the length of the content, in bytes, which its run is charged for.</summary>
    public int Length { get; }

    public List<(string Name, PdfObject[] Operands)> Operations { get; }

    /// <summary>Gets the error of the content after its last operation, thrown again by each run; <see langword="null"/> for none.</summary>
    public Exception? Error { get; }

    public static ParsedContent Parse(byte[] data)
    {
        var reader = new ContentStreamReader(data);
        var operands = new List<PdfObject>();
        var operations = new List<(string Name, PdfObject[] Operands)>();
        Exception? error = null;
        try
        {
            while (reader.TryRead(operands, out var name))
            {
                operations.Add((name, [.. operands]));
            }
        }
        catch (Exception ex) when (RenderCache.IsContentError(ex))
        {
            error = ex;
        }

        return new ParsedContent(data.Length, operations, error);
    }
}
