using Meziantou.Framework.Barcodes;

namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Reads the linear barcodes of an image: parallel scan lines at a few angles cross the image, and
/// <see cref="BarcodeDecoder.DecodeScanLines"/> reads the barcodes they cross.
/// </summary>
internal static class LinearLocator
{
    // The rows and columns first; the diagonals for the barcodes that are turned by another angle
    public static ReadOnlySpan<int> Angles => [0, 90, 30, 150, 60, 120];

    public static void Locate(ScanLineSource image, ReadOnlySpan<int> angles, Func<BarcodeType, bool> isIncluded, List<BarcodeImageResult> results, int maximumResults, CancellationToken cancellationToken)
    {
        // A few hundred lines across the image: close enough for the rows of the stacked GS1 DataBar symbols
        var step = Math.Max(1, Math.Min(image.Width, image.Height) / 300.0);
        foreach (var degrees in angles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var angle = degrees * Math.PI / 180;
            var direction = new PointD(Math.Cos(angle), Math.Sin(angle));
            var normal = new PointD(-direction.Y, direction.X);
            var (alongStart, alongEnd) = GetRange(image, direction);
            var (acrossStart, acrossEnd) = GetRange(image, normal);

            // The lines extend beyond the image, which is light there: a barcode cropped at its first and last bars has
            // quiet zones
            var margin = Math.Max(48, (alongEnd - alongStart) * 0.1);
            alongStart -= margin;
            alongEnd += margin;

            var offsets = new List<double>();
            var lines = new List<int[]>();
            for (var offset = acrossStart + (step / 2); offset < acrossEnd; offset += step)
            {
                offsets.Add(offset);
                lines.Add(image.GetRuns(direction, normal, offset, alongStart, alongEnd));
            }

            var decoded = BarcodeDecoder.DecodeScanLines(lines).Where(result => isIncluded(result.Type)).ToList();
            foreach (var group in MergeReadings(decoded, lines))
            {
                // The height the lines that read the barcode cover, against its length
                var length = (group.End - group.Start) / (double)image.UnitsPerPixel;
                if (!IsPlausible(group.Result, group.SupportLines, group.SupportLines * step / length, lines.Count))
                    continue;

                PointD At(int line, double position)
                {
                    var along = alongStart + (position / image.UnitsPerPixel);
                    return new PointD((direction.X * along) + (normal.X * offsets[line]), (direction.Y * along) + (normal.Y * offsets[line]));
                }

                PointD[] corners = [At(group.FirstLine, group.Start), At(group.FirstLine, group.End), At(group.LastLine, group.End), At(group.LastLine, group.Start)];
                Add(results, new BarcodeImageResult(group.Result, OrderCorners(corners)));
                if (results.Count >= maximumResults)
                    return;
            }
        }
    }

    /// <summary>
    /// Merges the readings of the same barcode: the lines of a blurred or damaged barcode do not all read it, and some read
    /// its add-on wrongly or not at all. Two readings with the same text (but for the add-on) are the same barcode when
    /// they overlap along the lines, and the line between them crosses bars, not the gap between two labels. The text of
    /// a barcode is the one most lines read; an add-on must be read by two lines.
    /// </summary>
    private static List<(BarcodeDecodeResult Result, int FirstLine, int LastLine, long Start, long End, int SupportLines)> MergeReadings(List<BarcodeDecodeResult> readings, List<int[]> lines)
    {
        static string GetMainText(BarcodeDecodeResult result)
        {
            if (result.Type is BarcodeType.Ean13 or BarcodeType.Ean8 or BarcodeType.UpcA or BarcodeType.UpcE)
            {
                var space = result.Text.IndexOf(' ', StringComparison.Ordinal);
                if (space >= 0)
                    return result.Text[..space];
            }

            return result.Text;
        }

        bool AreSameBarcode(BarcodeDecodeResult a, BarcodeDecodeResult b)
        {
            if (a.Type != b.Type || !string.Equals(GetMainText(a), GetMainText(b), StringComparison.Ordinal))
                return false;

            var start = Math.Max(a.Start, b.Start);
            var end = Math.Min(a.End, b.End);
            if (start >= end)
                return false;

            var (first, second) = a.FirstScanLine <= b.FirstScanLine ? (a, b) : (b, a);
            if (second.FirstScanLine <= first.LastScanLine + 1)
                return true;

            // A barcode has a bar every few modules: at least one per character, more than 10 transitions over its length
            var middle = (first.LastScanLine + second.FirstScanLine) / 2;
            return CountTransitions(lines[middle], start, end) >= 10;
        }

        var parents = Enumerable.Range(0, readings.Count).ToArray();
        int Find(int index)
        {
            while (parents[index] != index)
            {
                index = parents[index] = parents[parents[index]];
            }

            return index;
        }

        for (var i = 0; i < readings.Count; i++)
        {
            for (var j = i + 1; j < readings.Count; j++)
            {
                if (Find(i) != Find(j) && AreSameBarcode(readings[i], readings[j]))
                {
                    parents[Find(j)] = Find(i);
                }
            }
        }

        var merged = new List<(BarcodeDecodeResult Result, int FirstLine, int LastLine, long Start, long End, int SupportLines)>();
        foreach (var group in Enumerable.Range(0, readings.Count).GroupBy(Find))
        {
            var members = group.Select(index => readings[index]).ToList();
            static int GetWeight(BarcodeDecodeResult result) => result.LastScanLine - result.FirstScanLine + 1;
            var texts = members.GroupBy(result => result.Text, StringComparer.Ordinal).Select(texts => (Result: texts.First(), Weight: texts.Sum(GetWeight))).ToList();
            var withAddOn = texts.Where(text => text.Result.Text.Length > GetMainText(text.Result).Length && text.Weight >= 2).ToList();
            var best = (withAddOn.Count > 0 ? withAddOn : texts.Where(text => text.Result.Text.Length == GetMainText(text.Result).Length).DefaultIfEmpty(texts[0])).MaxBy(text => text.Weight).Result;
            merged.Add((best, members.Min(result => result.FirstScanLine), members.Max(result => result.LastScanLine), members.Min(result => result.Start), members.Max(result => result.End), members.Sum(GetWeight)));
        }

        return merged;
    }

    // The number of changes between dark and light of a line in a range of positions
    private static int CountTransitions(int[] runs, long start, long end)
    {
        var count = 0;
        var position = 0L;
        for (var i = 0; i < runs.Length - 1; i++)
        {
            position += runs[i];
            if (position > end)
                break;

            if (position > start)
            {
                count++;
            }
        }

        return count;
    }

    // The patterns of a photograph (text, textures, edges) can read as a barcode on a line, and a blurred barcode can read
    // with a wrong character that passes the check: a barcode needs two lines that agree. The symbologies whose check is
    // weak or missing can read in the bars of another barcode: Codabar and ITF need a few characters (4 with the start and
    // stop characters of Codabar, 6 digits of ITF), and these symbologies need lines that agree over a height of 4% of
    // their length, where the parts of other barcodes read on a few lines only.
    private static bool IsPlausible(BarcodeDecodeResult result, int supportLines, double heightRatio, int lineCount)
    {
        if (result.Type is BarcodeType.Codabar && result.Text.Length < 4)
            return false;

        if (result.Type is BarcodeType.Itf && result.Text.Length < 6)
            return false;

        // An image of a line or two
        if (lineCount <= 2)
            return true;

        if (supportLines < 2)
            return false;

        var isWeak = result.Type is BarcodeType.Code39 or BarcodeType.Codabar or BarcodeType.Itf or BarcodeType.Gs1DataBarLimited;
        return !isWeak || heightRatio >= 0.04;
    }

    // The range of the projections of the image on an axis through the origin
    private static (double Start, double End) GetRange(ScanLineSource image, PointD axis)
    {
        ReadOnlySpan<PointD> corners = [new(0, 0), new(image.Width, 0), new(0, image.Height), new(image.Width, image.Height)];
        var start = double.MaxValue;
        var end = double.MinValue;
        foreach (var corner in corners)
        {
            var projection = (corner.X * axis.X) + (corner.Y * axis.Y);
            start = Math.Min(start, projection);
            end = Math.Max(end, projection);
        }

        return (start, end);
    }

    // Clockwise in the image, from the corner nearest to its top-left
    private static PointD[] OrderCorners(PointD[] corners)
    {
        var center = new PointD(corners.Average(corner => corner.X), corners.Average(corner => corner.Y));
        var ordered = corners.OrderBy(corner => Math.Atan2(corner.Y - center.Y, corner.X - center.X)).ToArray();
        var first = Array.IndexOf(ordered, ordered.MinBy(corner => corner.X + corner.Y));
        return [.. ordered[first..], .. ordered[..first]];
    }

    // A barcode found at several angles is one result. The lines that cross a barcode at an angle can read other symbols in
    // its bars: a result over a barcode found before (at an angle closer to its bars) is not one.
    private static void Add(List<BarcodeImageResult> results, BarcodeImageResult result)
    {
        var bounds = GetBounds(result);
        foreach (var existing in results)
        {
            var existingBounds = GetBounds(existing);
            if (bounds.Left < existingBounds.Right && existingBounds.Left < bounds.Right && bounds.Top < existingBounds.Bottom && existingBounds.Top < bounds.Bottom)
                return;
        }

        results.Add(result);
    }

    // The bounding box of the corners, with a margin: a barcode crossed by a single line has a zero height
    private static (double Left, double Top, double Right, double Bottom) GetBounds(BarcodeImageResult result)
    {
        var length = result.Corners[0].DistanceTo(result.Corners[2]);
        var margin = Math.Max(2, length / 100);
        return (result.Corners.Min(corner => corner.X) - margin, result.Corners.Min(corner => corner.Y) - margin, result.Corners.Max(corner => corner.X) + margin, result.Corners.Max(corner => corner.Y) + margin);
    }
}
