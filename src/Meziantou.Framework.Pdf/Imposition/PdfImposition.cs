using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Arranges pages on sheets (N-up) and splits the spreads of scanned books into pages.</summary>
public static class PdfImposition
{
    private static readonly PdfName TrimBoxName = PdfName.Create("TrimBox");
    private static readonly PdfName BleedBoxName = PdfName.Create("BleedBox");
    private static readonly PdfName ArtBoxName = PdfName.Create("ArtBox");

    /// <summary>
    /// Creates a document whose sheets show several pages of a document each, such as two pages side by side (2-up) or
    /// four pages on a sheet (4-up): each page is scaled to fit its cell, keeping its proportions, centered, and upright
    /// whatever its rotation. The source document is not changed.
    /// </summary>
    /// <param name="source">The document whose pages are arranged.</param>
    /// <param name="options">The grid of the sheets.</param>
    /// <returns>The new document: <see cref="PdfNUpOptions.Columns"/> times <see cref="PdfNUpOptions.Rows"/> pages per sheet, the last sheet with the pages left.</returns>
    /// <exception cref="ArgumentException">The margins and the spacing leave no room for the cells.</exception>
    /// <exception cref="InvalidPdfContentException">A content stream of a page cannot be decoded.</exception>
    /// <remarks>The pages are copied as forms (<see cref="PdfDocument.CreateFormFromPage(PdfPage)"/>): their annotations, links and form fields are not part of the sheets.</remarks>
    public static PdfDocument CreateNUp(PdfDocument source, PdfNUpOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var target = new PdfDocument { Version = source.Version };
        if (source.Pages.Count == 0)
            return target;

        var columns = options.Columns;
        var rows = options.Rows;
        var first = source.Pages[0].DisplaySize;
        var sheet = options.SheetSize ?? new PdfSize(
            (columns * first.Width) + ((columns - 1) * options.Spacing) + (2 * options.Margin),
            (rows * first.Height) + ((rows - 1) * options.Spacing) + (2 * options.Margin));
        var cellWidth = (sheet.Width - (2 * options.Margin) - ((columns - 1) * options.Spacing)) / columns;
        var cellHeight = (sheet.Height - (2 * options.Margin) - ((rows - 1) * options.Spacing)) / rows;
        if (!(cellWidth > 0 && cellHeight > 0))
            throw new ArgumentException("The margin and the spacing leave no room for the pages", nameof(options));

        var perSheet = columns * rows;
        PdfContentBuilder? builder = null;
        for (var i = 0; i < source.Pages.Count; i++)
        {
            var slot = i % perSheet;
            if (slot == 0)
            {
                builder?.Complete();
                builder = target.Pages.Add(sheet).CreateContentBuilder();
            }

            var (column, row) = options.Order switch
            {
                PdfNUpOrder.Vertical => (slot / rows, slot % rows),
                PdfNUpOrder.HorizontalRightToLeft => (columns - 1 - (slot % columns), slot / columns),
                _ => (slot % columns, slot / columns),
            };

            var page = source.Pages[i];
            var size = page.DisplaySize;
            if (!(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height)))
                continue;

            // The cell, from the top left corner of the sheet; the page scaled to fit it and centered
            var cellLeft = options.Margin + (column * (cellWidth + options.Spacing));
            var cellBottom = sheet.Height - options.Margin - ((row + 1) * cellHeight) - (row * options.Spacing);
            var scale = Math.Min(cellWidth / size.Width, cellHeight / size.Height);
            var x = cellLeft + ((cellWidth - (size.Width * scale)) / 2);
            var y = cellBottom + ((cellHeight - (size.Height * scale)) / 2);
            var form = target.CreateFormFromPage(page);
            builder!.SaveGraphicsState()
                .Transform(PdfMatrix.CreateScale(scale, scale) * PdfMatrix.CreateTranslation(x, y))
                .DrawForm(form)
                .RestoreGraphicsState();
            if (options.DrawBorders)
            {
                builder.SaveGraphicsState()
                    .SetStrokeGray(0.6)
                    .SetLineWidth(0.5)
                    .Rectangle(x, y, size.Width * scale, size.Height * scale)
                    .Stroke()
                    .RestoreGraphicsState();
            }
        }

        builder?.Complete();
        return target;
    }

    /// <summary>
    /// Splits the pages that show two pages side by side, such as the spreads of a scanned book, into two pages: the page
    /// shows its left half, and a copy of it, inserted after it, its right half (or the reverse, from right to left). The
    /// halves are the halves of the page as it is displayed, its rotation applied.
    /// </summary>
    /// <param name="document">The document, changed in place.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfSpreadSplitOptions.Default"/>.</param>
    /// <returns>The number of pages split.</returns>
    /// <exception cref="ArgumentException">The gutter is wider than a page.</exception>
    /// <remarks>
    /// The halves are crop boxes: the content of the page is kept, and the copy shares it. The trim, bleed and art boxes
    /// of the pages split are removed, since they describe the spread.
    /// </remarks>
    public static int SplitSpreads(PdfDocument document, PdfSpreadSplitOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= PdfSpreadSplitOptions.Default;

        var split = 0;
        for (var i = 0; i < document.Pages.Count; i++)
        {
            var page = document.Pages[i];
            var size = page.DisplaySize;
            if (!(size.Width > 0 && size.Height > 0 && double.IsFinite(size.Width) && double.IsFinite(size.Height)))
                continue;

            if (options.OnlyLandscapePages && size.Width <= size.Height)
                continue;

            var half = (size.Width - options.Gutter) / 2;
            if (!(half > 0))
                throw new ArgumentException($"The gutter is wider than the page {i}", nameof(options));

            var left = new PdfRectangle(0, 0, half, size.Height);
            var right = new PdfRectangle(size.Width - half, 0, size.Width, size.Height);
            var copy = document.Pages.Duplicate(i);
            SetVisibleHalf(page, options.RightToLeft ? right : left);
            SetVisibleHalf(copy, options.RightToLeft ? left : right);
            split++;
            i++;
        }

        return split;
    }

    /// <summary>Sets the crop box of a page to a rectangle of the page as it is displayed.</summary>
    private static void SetVisibleHalf(PdfPage page, PdfRectangle displayed)
    {
        var matrix = page.DisplayToUserSpace;
        var (x1, y1) = matrix.Transform(displayed.Left, displayed.Bottom);
        var (x2, y2) = matrix.Transform(displayed.Right, displayed.Top);
        page.CropBox = new PdfRectangle(x1, y1, x2, y2);
        page.Dictionary.Remove(TrimBoxName);
        page.Dictionary.Remove(BleedBoxName);
        page.Dictionary.Remove(ArtBoxName);
    }
}
