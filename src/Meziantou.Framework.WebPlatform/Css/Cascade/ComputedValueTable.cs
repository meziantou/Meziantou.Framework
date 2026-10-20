namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The computed values that the styles of a style engine share: each distinct value is stored once, and the styles that compute
/// most properties store its index (2 bytes) instead of a reference to their own string (see <see cref="ComputedStyle"/>). The
/// properties that are not inherited are computed for each element, and most of them have the same few values (e.g.
/// <c>0px</c>, <c>none</c>, <c>auto</c>, <c>currentcolor</c>): a document has a few hundred distinct computed values for
/// millions of computed values.
/// </summary>
/// <remarks>
/// The values are never removed, so the table is bounded: it only keeps the short values (<see cref="MaxValueLength"/>), at most
/// <see cref="MaxCount"/> of them (e.g. the values of a running animation would otherwise make it grow). The style engine replaces
/// a full table with a new one (see <see cref="StyleEngine.ComputedValueTable"/>); the styles keep the table their indexes refer to.
/// </remarks>
internal sealed class ComputedValueTable
{
    /// <summary>
    /// The maximum number of values (a document has a few hundred distinct short values, but the values of a running animation
    /// are added as they change: a full table is replaced, and freed with the last style that uses it). The indexes are at most
    /// 15-bit: the styles use the 16th bit for their own values.
    /// </summary>
    public const int MaxCount = 4096;

    /// <summary>The maximum length of the values of the table (the longer values rarely repeat, or are inherited, which shares them).</summary>
    private const int MaxValueLength = 128;

    private readonly Dictionary<string, ushort> _indexes = new(StringComparer.Ordinal);
    private string[] _values = new string[64];

    public bool IsFull => _indexes.Count == MaxCount;

    /// <summary>Returns the value at an index returned by <see cref="TryGetIndex"/>.</summary>
    public string this[int index] => _values[index];

    /// <summary>Returns the index of a value, adding it to the table, or false when the value is too long or the table is full.</summary>
    public bool TryGetIndex(string value, out ushort index)
    {
        if (_indexes.TryGetValue(value, out index))
            return true;

        var count = _indexes.Count;
        if (value.Length > MaxValueLength || count == MaxCount)
            return false;

        if (count == _values.Length)
        {
            Array.Resize(ref _values, count * 2);
        }

        _values[count] = value;
        index = (ushort)count;
        _indexes.Add(value, index);
        return true;
    }

    /// <summary>Returns the string of the table equal to the value, or the value itself when it is not in the table (see <see cref="TryGetIndex"/>).</summary>
    public string Share(string value) => TryGetIndex(value, out var index) ? _values[index] : value;
}
