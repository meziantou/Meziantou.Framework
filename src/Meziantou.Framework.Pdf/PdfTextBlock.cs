namespace Meziantou.Framework.Pdf;

/// <summary>A block of the text of a page: lines that blank spaces of the page do not separate, such as a paragraph or a part of a column (see <see cref="PdfTextLayout.Blocks"/>).</summary>
public sealed class PdfTextBlock
{
    internal PdfTextBlock(IReadOnlyList<PdfTextLine> lines)
    {
        Lines = lines;
        Text = string.Join('\n', lines.Select(line => line.Text));
        Bounds = Union(lines);
    }

    /// <summary>Gets the text of the block: the text of its lines separated by line feeds.</summary>
    public string Text { get; }

    /// <summary>Gets the lines of the block.</summary>
    public IReadOnlyList<PdfTextLine> Lines { get; }

    /// <summary>Gets the smallest rectangle that contains the bounds of the lines of the block.</summary>
    public PdfRectangle Bounds { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;

    private static PdfRectangle Union(IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count == 0)
            return default;

        var left = double.PositiveInfinity;
        var bottom = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var top = double.NegativeInfinity;
        foreach (var line in lines)
        {
            left = Math.Min(left, line.Bounds.Left);
            bottom = Math.Min(bottom, line.Bounds.Bottom);
            right = Math.Max(right, line.Bounds.Right);
            top = Math.Max(top, line.Bounds.Top);
        }

        return new PdfRectangle(left, bottom, right, top);
    }
}
