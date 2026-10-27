namespace Meziantou.Framework.Fonts;

/// <summary>The characters a face has a glyph for: sorted and disjoint ranges of code points.</summary>
internal sealed class SystemFontCoverage
{
    // What an instance costs besides its ranges: the object and the header of its array.
    private const int Overhead = 48;

    private readonly int[] _ranges;

    public SystemFontCoverage(int[] ranges)
    {
        _ranges = ranges;
    }

    /// <summary>Gets the coverage of a face whose character map cannot be read: no character.</summary>
    public static SystemFontCoverage Empty { get; } = new([]);

    /// <summary>Gets the number of bytes the coverage keeps in memory, which a <see cref="SystemFontCoverageCache"/> is charged.</summary>
    public long Size => Overhead + ((long)_ranges.Length * sizeof(int));

    /// <summary>Gets the number of ranges.</summary>
    public int RangeCount => _ranges.Length / 2;

    public bool Contains(int codePoint)
    {
        // The last range whose first character is not after the character.
        var low = 0;
        var high = (_ranges.Length / 2) - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (_ranges[middle * 2] <= codePoint)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high >= 0 && codePoint <= _ranges[(high * 2) + 1];
    }
}
