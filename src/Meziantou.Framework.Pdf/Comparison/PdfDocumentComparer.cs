using System.Globalization;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Compares what the pages of two documents show (<see cref="PdfPageElements"/>): the pages added or removed, their
/// size and rotation, their text and its position, the images, the paths and the shadings they paint. The pages are
/// compared in order: the page n of a document with the page n of the other one.
/// </summary>
/// <remarks>
/// The comparison is structural: two documents written differently (object numbers, compression, object streams) that
/// show the same elements are equivalent, and a change of the content is reported by kind rather than as different
/// pixels. The colors are compared by the operators that set them, and the elements by their bounding boxes.
/// </remarks>
public static class PdfDocumentComparer
{
    private const int MaxDiffCells = 4_000_000;

    /// <summary>Compares two documents.</summary>
    /// <param name="expected">The first document, such as the expected one.</param>
    /// <param name="actual">The second document, such as the one produced.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfComparisonOptions.Default"/>.</param>
    /// <returns>The changes of each page.</returns>
    /// <exception cref="PdfException">The page tree of a document cannot be read.</exception>
    public static PdfComparisonResult Compare(PdfDocument expected, PdfDocument actual, PdfComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        options ??= PdfComparisonOptions.Default;

        var pages = new List<PdfPageComparison>();
        var count = Math.Max(expected.Pages.Count, actual.Pages.Count);
        for (var i = 0; i < count; i++)
        {
            if (i >= actual.Pages.Count)
            {
                pages.Add(new PdfPageComparison(i, actualPageIndex: null, PdfPageChangeKinds.Removed, ["- page"]));
            }
            else if (i >= expected.Pages.Count)
            {
                pages.Add(new PdfPageComparison(expectedPageIndex: null, i, PdfPageChangeKinds.Added, ["+ page"]));
            }
            else
            {
                pages.Add(Compare(PdfPageElements.Create(expected.Pages[i]), PdfPageElements.Create(actual.Pages[i]), i, i, options));
            }
        }

        return new PdfComparisonResult(pages);
    }

    /// <summary>Compares the elements of two pages.</summary>
    /// <param name="expected">The elements of the first page.</param>
    /// <param name="actual">The elements of the second page.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfComparisonOptions.Default"/>.</param>
    /// <returns>The changes of the page.</returns>
    public static PdfPageComparison Compare(PdfPageElements expected, PdfPageElements actual, PdfComparisonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return Compare(expected, actual, expectedIndex: null, actualIndex: null, options ?? PdfComparisonOptions.Default);
    }

    private static PdfPageComparison Compare(PdfPageElements expected, PdfPageElements actual, int? expectedIndex, int? actualIndex, PdfComparisonOptions options)
    {
        var tolerance = options.Tolerance;
        var changes = PdfPageChangeKinds.None;
        var details = new List<string>();
        if (Math.Abs(expected.Width - actual.Width) > tolerance || Math.Abs(expected.Height - actual.Height) > tolerance)
        {
            changes |= PdfPageChangeKinds.Size;
            details.Add(string.Create(CultureInfo.InvariantCulture, $"~ size {expected.Width:0.##} x {expected.Height:0.##} -> {actual.Width:0.##} x {actual.Height:0.##}"));
        }

        if (expected.Rotation != actual.Rotation)
        {
            changes |= PdfPageChangeKinds.Rotation;
            details.Add(string.Create(CultureInfo.InvariantCulture, $"~ rotation {expected.Rotation} -> {actual.Rotation}"));
        }

        changes |= CompareText(expected.Text, actual.Text, tolerance, options.MaxDetails, details);
        changes |= CompareElements(expected.Images, actual.Images, image => (image.Width, image.Height, image.Digest), image => image.Bounds, tolerance, options.MaxDetails, details, PdfPageChangeKinds.Images, PdfPageChangeKinds.ImagePlacements);
        changes |= CompareElements(expected.Paths, actual.Paths, path => (path.Paint, path.FillColor, path.StrokeColor), path => path.Bounds, tolerance, options.MaxDetails, details, PdfPageChangeKinds.Paths, PdfPageChangeKinds.Paths);
        changes |= CompareElements(expected.Shadings, actual.Shadings, shading => (shading.ShadingType, shading.Digest), (e, a) => AreClose(e.Matrix, a.Matrix, tolerance), shading => shading.Matrix.ToString(), options.MaxDetails, details, PdfPageChangeKinds.Shadings, PdfPageChangeKinds.Shadings);
        return new PdfPageComparison(expectedIndex, actualIndex, changes, details);
    }

    /// <summary>Compares the lines of text: a diff of their texts (the longest common subsequence), then the positions of the lines kept.</summary>
    private static PdfPageChangeKinds CompareText(IReadOnlyList<PdfTextElement> expected, IReadOnlyList<PdfTextElement> actual, double tolerance, int maxDetails, List<string> details)
    {
        var changes = PdfPageChangeKinds.None;
        var removed = new List<string>();
        var added = new List<string>();
        var moved = new List<string>();
        foreach (var (kind, e, a) in Diff(expected, actual))
        {
            switch (kind)
            {
                case '-':
                    removed.Add("- text " + e);
                    break;
                case '+':
                    added.Add("+ text " + a);
                    break;
                default:
                    if (!AreClose(e!.Bounds, a!.Bounds, tolerance))
                    {
                        moved.Add($"~ text \"{a.Text}\" {e.Bounds} -> {a.Bounds}");
                    }

                    break;
            }
        }

        if (removed.Count > 0 || added.Count > 0)
        {
            changes |= PdfPageChangeKinds.Text;
            AddDetails(details, removed, maxDetails);
            AddDetails(details, added, maxDetails);
        }

        if (moved.Count > 0)
        {
            changes |= PdfPageChangeKinds.TextGeometry;
            AddDetails(details, moved, maxDetails);
        }

        return changes;
    }

    /// <summary>The longest common subsequence of the texts of two lists of lines, as removed, added and kept lines.</summary>
    private static List<(char Kind, PdfTextElement? Expected, PdfTextElement? Actual)> Diff(IReadOnlyList<PdfTextElement> expected, IReadOnlyList<PdfTextElement> actual)
    {
        var result = new List<(char, PdfTextElement?, PdfTextElement?)>();

        // The common prefix and suffix first: the table is built for the lines between them only
        var start = 0;
        while (start < expected.Count && start < actual.Count && string.Equals(expected[start].Text, actual[start].Text, StringComparison.Ordinal))
        {
            start++;
        }

        var endExpected = expected.Count;
        var endActual = actual.Count;
        while (endExpected > start && endActual > start && string.Equals(expected[endExpected - 1].Text, actual[endActual - 1].Text, StringComparison.Ordinal))
        {
            endExpected--;
            endActual--;
        }

        for (var i = 0; i < start; i++)
        {
            result.Add(('=', expected[i], actual[i]));
        }

        var n = endExpected - start;
        var m = endActual - start;
        if ((long)n * m > MaxDiffCells)
        {
            // Too large for a table: every line in between is removed and added
            for (var i = start; i < endExpected; i++)
            {
                result.Add(('-', expected[i], null));
            }

            for (var j = start; j < endActual; j++)
            {
                result.Add(('+', null, actual[j]));
            }
        }
        else
        {
            var lengths = new int[(n + 1) * (m + 1)];
            int Length(int i, int j) => lengths[(i * (m + 1)) + j];
            for (var i = n - 1; i >= 0; i--)
            {
                for (var j = m - 1; j >= 0; j--)
                {
                    lengths[(i * (m + 1)) + j] = string.Equals(expected[start + i].Text, actual[start + j].Text, StringComparison.Ordinal) ? Length(i + 1, j + 1) + 1 : Math.Max(Length(i + 1, j), Length(i, j + 1));
                }
            }

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(expected[start + x].Text, actual[start + y].Text, StringComparison.Ordinal))
                {
                    result.Add(('=', expected[start + x], actual[start + y]));
                    x++;
                    y++;
                }
                else if (Length(x + 1, y) >= Length(x, y + 1))
                {
                    result.Add(('-', expected[start + x], null));
                    x++;
                }
                else
                {
                    result.Add(('+', null, actual[start + y]));
                    y++;
                }
            }

            for (; x < n; x++)
            {
                result.Add(('-', expected[start + x], null));
            }

            for (; y < m; y++)
            {
                result.Add(('+', null, actual[start + y]));
            }
        }

        for (var i = 0; i < expected.Count - endExpected; i++)
        {
            result.Add(('=', expected[endExpected + i], actual[endActual + i]));
        }

        return result;
    }

    /// <summary>
    /// Compares two lists of elements: an element of the first list matches an element of the second one with the same key
    /// at the same place; the elements left with the same key are moved; the others are removed or added.
    /// </summary>
    private static PdfPageChangeKinds CompareElements<T, TKey>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, Func<T, TKey> getKey, Func<T, PdfRectangle> getBounds, double tolerance, int maxDetails, List<string> details, PdfPageChangeKinds contentChange, PdfPageChangeKinds placementChange)
        where T : notnull
        where TKey : notnull
    {
        return CompareElements(expected, actual, getKey, (e, a) => AreClose(getBounds(e), getBounds(a), tolerance), item => getBounds(item).ToString(), maxDetails, details, contentChange, placementChange);
    }

    /// <summary>Matches the elements of the same key, at the same place first.</summary>
    private static PdfPageChangeKinds CompareElements<T, TKey>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, Func<T, TKey> getKey, Func<T, T, bool> isAtSamePlace, Func<T, string> getPlacement, int maxDetails, List<string> details, PdfPageChangeKinds contentChange, PdfPageChangeKinds placementChange)
        where T : notnull
        where TKey : notnull
    {
        var remaining = new Dictionary<TKey, List<T>>();
        foreach (var item in actual)
        {
            var key = getKey(item);
            if (!remaining.TryGetValue(key, out var list))
            {
                list = [];
                remaining.Add(key, list);
            }

            list.Add(item);
        }

        // The elements at the same place first, then those that moved
        var unmatched = new List<T>();
        foreach (var item in expected)
        {
            if (remaining.TryGetValue(getKey(item), out var candidates) && candidates.FindIndex(candidate => isAtSamePlace(item, candidate)) is >= 0 and var index)
            {
                candidates.RemoveAt(index);
            }
            else
            {
                unmatched.Add(item);
            }
        }

        var moved = new List<string>();
        var removed = new List<string>();
        foreach (var item in unmatched)
        {
            if (remaining.TryGetValue(getKey(item), out var candidates) && candidates.Count > 0)
            {
                var candidate = candidates[0];
                candidates.RemoveAt(0);
                moved.Add($"~ {item} -> {getPlacement(candidate)}");
            }
            else
            {
                removed.Add("- " + item);
            }
        }

        var added = remaining.Values.SelectMany(list => list).Select(item => "+ " + item).ToList();
        var changes = PdfPageChangeKinds.None;
        if (removed.Count > 0 || added.Count > 0)
        {
            changes |= contentChange;
            AddDetails(details, removed, maxDetails);
            AddDetails(details, added, maxDetails);
        }

        if (moved.Count > 0)
        {
            changes |= placementChange;
            AddDetails(details, moved, maxDetails);
        }

        return changes;
    }

    private static void AddDetails(List<string> details, List<string> items, int maxDetails)
    {
        details.AddRange(items.Take(maxDetails));
        if (items.Count > maxDetails)
        {
            details.Add(string.Create(CultureInfo.InvariantCulture, $"  ... and {items.Count - maxDetails} more"));
        }
    }

    private static bool AreClose(PdfMatrix expected, PdfMatrix actual, double tolerance)
    {
        return Math.Abs(expected.A - actual.A) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.C - actual.C) <= tolerance
            && Math.Abs(expected.D - actual.D) <= tolerance && Math.Abs(expected.E - actual.E) <= tolerance && Math.Abs(expected.F - actual.F) <= tolerance;
    }

    private static bool AreClose(PdfRectangle left, PdfRectangle right, double tolerance)
    {
        return Math.Abs(left.Left - right.Left) <= tolerance && Math.Abs(left.Bottom - right.Bottom) <= tolerance
            && Math.Abs(left.Right - right.Right) <= tolerance && Math.Abs(left.Top - right.Top) <= tolerance;
    }
}
