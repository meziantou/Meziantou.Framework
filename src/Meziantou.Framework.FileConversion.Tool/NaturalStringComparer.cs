namespace Meziantou.Framework.FileConversionTool;

/// <summary>
/// Compares strings with their runs of ASCII digits compared by numeric value (<c>page2</c> before <c>page10</c>), and the
/// rest ordinally without regard to ASCII case. It does not depend on the culture.
/// </summary>
internal sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        if (x is null)
            return -1;

        if (y is null)
            return 1;

        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var digitsX = x.AsSpan(startX, i - startX).TrimStart('0');
                var digitsY = y.AsSpan(startY, j - startY).TrimStart('0');
                if (digitsX.Length != digitsY.Length)
                    return digitsX.Length.CompareTo(digitsY.Length);

                var result = digitsX.SequenceCompareTo(digitsY);
                if (result is not 0)
                    return result;

                // 01 and 1 have the same value: the shortest one first
                var lengthResult = (i - startX).CompareTo(j - startY);
                if (lengthResult is not 0)
                    return lengthResult;

                continue;
            }

            var cx = ToAsciiLower(x[i]);
            var cy = ToAsciiLower(y[j]);
            if (cx != cy)
                return cx.CompareTo(cy);

            i++;
            j++;
        }

        var remaining = (x.Length - i).CompareTo(y.Length - j);
        if (remaining is not 0)
            return remaining;

        // Equal without regard to case: an ordinal comparison keeps the order total
        return string.CompareOrdinal(x, y);
    }

    private static char ToAsciiLower(char c) => char.IsAsciiLetterUpper(c) ? (char)(c | 0x20) : c;
}
