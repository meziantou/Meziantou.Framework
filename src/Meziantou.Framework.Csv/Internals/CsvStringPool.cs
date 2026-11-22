namespace Meziantou.Framework.Csv.Internals;

/// <summary>Reuses the strings of short values. Not thread-safe: each reader has its own pool.</summary>
internal sealed class CsvStringPool
{
    public const int MaxLength = 32;
    private const int MaxCount = 4096;

    private readonly HashSet<string> _strings = new(StringComparer.Ordinal);
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _lookup;

    public CsvStringPool()
    {
        _lookup = _strings.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public string GetOrAdd(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return "";

        if (value.Length > MaxLength)
            return new string(value);

        if (_lookup.TryGetValue(value, out var result))
            return result;

        result = new string(value);
        if (_strings.Count < MaxCount)
        {
            _strings.Add(result);
        }

        return result;
    }
}
