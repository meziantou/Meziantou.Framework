using System.Collections.Concurrent;

namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// The value of the widths of the elements of a GS1 DataBar character: the inverse of
/// <see cref="DataBarHelper.GetWidths"/>, built by enumerating the values of each family of characters.
/// </summary>
internal static class DataBarWidths
{
    private static readonly ConcurrentDictionary<(int Elements, int Modules, int MaxWidth, bool NoNarrow), Dictionary<long, int>> Tables = new();

    /// <summary>Gets the value of the widths, or -1 when they are not a character of the family.</summary>
    public static int GetValue(ReadOnlySpan<int> widths, int modules, int maxWidth, bool noNarrow)
    {
        var table = Tables.GetOrAdd((widths.Length, modules, maxWidth, noNarrow), static key => CreateTable(key.Elements, key.Modules, key.MaxWidth, key.NoNarrow));
        return table.TryGetValue(GetKey(widths), out var value) ? value : -1;
    }

    private static long GetKey(ReadOnlySpan<int> widths)
    {
        var key = 0L;
        foreach (var width in widths)
        {
            if (width is < 0 or > 15)
                return -1;

            key = (key << 4) | (long)width;
        }

        return key;
    }

    private static Dictionary<long, int> CreateTable(int elements, int modules, int maxWidth, bool noNarrow)
    {
        // The characters of the family are the combinations of widths of 1 to maxWidth modules, with at least one element
        // of 1 module unless noNarrow is set: their values are 0 to their count minus 1
        var count = CountCombinations(elements, modules, maxWidth, requireNarrow: !noNarrow, hasNarrow: false);
        var table = new Dictionary<long, int>(count);
        Span<int> widths = stackalloc int[elements];
        for (var value = 0; value < count; value++)
        {
            DataBarHelper.GetWidths(widths, value, modules, maxWidth, noNarrow);
            table[GetKey(widths)] = value;
        }

        return table;
    }

    private static int CountCombinations(int elements, int modules, int maxWidth, bool requireNarrow, bool hasNarrow)
    {
        if (elements is 1)
            return modules >= 1 && modules <= maxWidth && (!requireNarrow || hasNarrow || modules is 1) ? 1 : 0;

        var count = 0;
        for (var width = 1; width <= maxWidth && width <= modules - (elements - 1); width++)
        {
            count += CountCombinations(elements - 1, modules - width, maxWidth, requireNarrow, hasNarrow || width is 1);
        }

        return count;
    }
}
