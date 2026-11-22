namespace Meziantou.Framework.Csv;

/// <summary>The names of the columns, read from the header row.</summary>
public sealed class CsvHeader
{
    // Comparing a few names is faster than hashing them, and does not allocate a dictionary for each file
    private const int MaxLinearSearchColumnCount = 16;

    private readonly string[] _columnNames;
    private readonly IEqualityComparer<string> _comparer;
    private readonly Dictionary<string, int>? _indices;

    internal CsvHeader(string[] columnNames, IEqualityComparer<string> comparer)
    {
        _columnNames = columnNames;
        _comparer = comparer;
        if (columnNames.Length > MaxLinearSearchColumnCount)
        {
            _indices = new Dictionary<string, int>(columnNames.Length, comparer);
            for (var i = 0; i < columnNames.Length; i++)
            {
                // When two columns have the same name, the name finds the first one
                _indices.TryAdd(columnNames[i], i);
            }
        }
    }

    /// <summary>Gets the names of the columns, in order.</summary>
    public IReadOnlyList<string> ColumnNames => _columnNames;

    /// <summary>Gets the number of columns of the header row.</summary>
    public int ColumnCount => _columnNames.Length;

    /// <summary>Gets the 0-based index of the column with the specified name.</summary>
    /// <param name="columnName">The name of the column.</param>
    /// <returns>The index of the first column with this name, or -1 when the header has no such column.</returns>
    public int IndexOf(string columnName) => TryGetIndex(columnName, out var index) ? index : -1;

    /// <summary>Gets the 0-based index of the column with the specified name.</summary>
    /// <param name="columnName">The name of the column.</param>
    /// <param name="index">The index of the first column with this name.</param>
    /// <returns><see langword="true"/> when the header has a column with this name; otherwise, <see langword="false"/>.</returns>
    public bool TryGetIndex(string columnName, out int index)
    {
        ArgumentNullException.ThrowIfNull(columnName);
        if (_indices is not null)
            return _indices.TryGetValue(columnName, out index);

        // The first column with the name, as with the dictionary
        var columnNames = _columnNames;
        for (var i = 0; i < columnNames.Length; i++)
        {
            if (_comparer.Equals(columnNames[i], columnName))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => string.Join(", ", _columnNames);
}
