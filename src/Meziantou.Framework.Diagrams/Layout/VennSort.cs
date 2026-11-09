namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// The stable sort of JavaScript arrays, with a comparison that returns a number: a negative number puts the first item
/// first, a positive number the second, and zero or NaN keeps their order. <see cref="List{T}.Sort(Comparison{T})"/> is
/// not stable and can throw on such comparisons.
/// </summary>
internal static class VennSort
{
    public static void StableSort<T>(List<T> items, Func<T, T, double> compare)
    {
        if (items.Count < 2)
            return;

        var buffer = new T[items.Count];
        var source = items.ToArray();
        for (var width = 1; width < source.Length; width *= 2)
        {
            for (var start = 0; start < source.Length; start += 2 * width)
            {
                var middle = Math.Min(start + width, source.Length);
                var end = Math.Min(start + (2 * width), source.Length);
                int left = start, right = middle, output = start;
                while (left < middle && right < end)
                {
                    // Take the right item only when it must come first: equal items keep their order
                    buffer[output++] = compare(source[left], source[right]) > 0 ? source[right++] : source[left++];
                }

                while (left < middle)
                {
                    buffer[output++] = source[left++];
                }

                while (right < end)
                {
                    buffer[output++] = source[right++];
                }
            }

            (source, buffer) = (buffer, source);
        }

        for (var i = 0; i < source.Length; i++)
        {
            items[i] = source[i];
        }
    }
}
