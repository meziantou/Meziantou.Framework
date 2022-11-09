// https://github.com/dotnet/aspnetcore/blob/e43027e506f5b004e3c3609f588c3e31f41aebfc/src/Http/WebUtilities/src/QueryHelpers.cs
using Microsoft.Extensions.Primitives;

namespace Meziantou.AspNetCore.Components.Internals;

internal struct KeyValueAccumulator
{
    private Dictionary<string, StringValues> _accumulator;
    private Dictionary<string, List<string?>> _expandingAccumulator;

    public void Append(string key, string value)
    {
        _accumulator ??= new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        if (_accumulator.TryGetValue(key, out var values))
        {
            if (values.Count == 0)
            {
                // Marker entry for this key to indicate entry already in expanding list dictionary
                _expandingAccumulator[key].Add(value);
            }
            else if (values.Count == 1)
            {
                // Second value for this key
                _accumulator[key] = new string?[] { values[0], value };
            }
            else
            {
                // Third value for this key
                // Add zero count entry and move to data to expanding list dictionary
                _accumulator[key] = default;
                _expandingAccumulator ??= new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);

                // Already 3 entries so use starting allocated as 8; then use List's expansion mechanism for more
                var list = new List<string?>(8);
                var array = values.ToArray();

                list.Add(array[0]);
                list.Add(array[1]);
                list.Add(value);

                _expandingAccumulator[key] = list;
            }
        }
        else
        {
            // First value for this key
            _accumulator[key] = new StringValues(value);
        }

        ValueCount++;
    }

    public readonly bool HasValues => ValueCount > 0;

    public readonly int KeyCount => _accumulator?.Count ?? 0;

    public int ValueCount { get; private set; }

    public readonly Dictionary<string, StringValues> GetResults()
    {
        if (_expandingAccumulator != null)
        {
            // Coalesce count 3+ multi-value entries into _accumulator dictionary
            foreach (var entry in _expandingAccumulator)
            {
                _accumulator[entry.Key] = new StringValues(entry.Value.ToArray());
            }
        }

        return _accumulator ?? new Dictionary<string, StringValues>(0, StringComparer.OrdinalIgnoreCase);
    }
}
