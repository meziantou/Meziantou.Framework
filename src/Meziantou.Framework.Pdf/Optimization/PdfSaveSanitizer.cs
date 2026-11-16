using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Repairs harmless issues of a file that some readers handle badly (<see cref="PdfSanitizerRepairKind"/>), with one
/// incremental update that keeps the bytes of the file: a file that has none of the issues is returned as it is.
/// </summary>
public static class PdfSaveSanitizer
{
    private const double Epsilon = 1e-6;

    private static readonly PdfName OutlinesName = PdfName.Create("Outlines");
    private static readonly PdfName FirstName = PdfName.Create("First");

    /// <summary>Repairs a file.</summary>
    /// <param name="file">The file.</param>
    /// <param name="options">The options to read the file with, or <see langword="null"/> for the default options.</param>
    /// <returns>The repaired file and the repairs.</returns>
    /// <exception cref="PdfException">The file cannot be read.</exception>
    /// <exception cref="InvalidOperationException">The file needs repairs, and is damaged: the reader repaired its structure, so it cannot be updated incrementally.</exception>
    public static PdfSanitizationResult Sanitize(ReadOnlyMemory<byte> file, PdfReadOptions? options = null)
    {
        var document = PdfDocument.Open(file, options);
        var repairs = new List<PdfSanitizerRepair>();
        RepairOutline(document, repairs);
        var index = 0;
        foreach (var page in document.Pages)
        {
            RepairCropBox(page, index, repairs);
            index++;
        }

        if (repairs.Count == 0)
            return new PdfSanitizationResult(file, repairs);

        if (document.IsRepaired)
            throw new InvalidOperationException("The cross-reference information of the file was rebuilt: the file cannot be updated incrementally. Save it in full.");

        using var output = new MemoryStream();
        document.SaveIncremental(output);
        return new PdfSanitizationResult(output.ToArray(), repairs);
    }

    private static void RepairOutline(PdfDocument document, List<PdfSanitizerRepair> repairs)
    {
        var catalog = document.Catalog;
        if (!catalog.ContainsKey(OutlinesName))
            return;

        PdfObject? outlines;
        try
        {
            outlines = catalog.GetResolved(OutlinesName);
        }
        catch (PdfException)
        {
            outlines = null;
        }

        if (outlines is not PdfDictionary root)
        {
            catalog.Remove(OutlinesName);
            repairs.Add(new PdfSanitizerRepair(PdfSanitizerRepairKind.DanglingOutlineRoot, "Removed the outline root, which is not an outline dictionary", pageIndex: null));
            return;
        }

        PdfObject? first;
        try
        {
            first = root.GetResolved(FirstName);
        }
        catch (PdfException)
        {
            first = null;
        }

        if (first is not PdfDictionary)
        {
            catalog.Remove(OutlinesName);
            repairs.Add(new PdfSanitizerRepair(PdfSanitizerRepairKind.EmptyOutlineRoot, "Removed the outline root, which has no item", pageIndex: null));
        }
    }

    private static void RepairCropBox(PdfPage page, int index, List<PdfSanitizerRepair> repairs)
    {
        if (page.Dictionary.GetResolved(PdfNames.CropBox) is not PdfArray array || !PdfRectangle.TryRead(array, out var cropBox))
            return;

        var mediaBox = page.MediaBox;
        var left = Math.Max(cropBox.Left, mediaBox.Left);
        var bottom = Math.Max(cropBox.Bottom, mediaBox.Bottom);
        var right = Math.Min(cropBox.Right, mediaBox.Right);
        var top = Math.Min(cropBox.Top, mediaBox.Top);
        if (cropBox.Width <= Epsilon || cropBox.Height <= Epsilon || right - left <= Epsilon || top - bottom <= Epsilon)
        {
            page.Dictionary.Remove(PdfNames.CropBox);
            repairs.Add(new PdfSanitizerRepair(PdfSanitizerRepairKind.DegenerateCropBox, $"Removed the crop box {cropBox} of the page {index + 1}, which has no area inside the media box", index));
            return;
        }

        if (left - cropBox.Left > Epsilon || bottom - cropBox.Bottom > Epsilon || cropBox.Right - right > Epsilon || cropBox.Top - top > Epsilon)
        {
            var intersection = new PdfRectangle(left, bottom, right, top);
            page.Dictionary[PdfNames.CropBox] = intersection.ToArray();
            repairs.Add(new PdfSanitizerRepair(PdfSanitizerRepairKind.CropBoxOutsideMediaBox, $"Set the crop box {cropBox} of the page {index + 1} to its intersection with the media box, {intersection}", index));
        }
    }
}
