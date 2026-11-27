namespace Meziantou.Framework.Csv;

/// <summary>The names of the columns, read from the header row.</summary>
public sealed class CsvHeader
{
    // Comparing a few names is faster than hashing them, and does not allocate a dictionary for each file
    private const int MaxLinearSearchColumnCount = 16;

    private readonly string[] _columnNames;
    private readonly IEqualityComparer<string> _comparer;
    private readonly Dictionary<string, int>? _indices;

    // The instance of the name last found for each column, and the column after the last one found: see TryGetIndex. A header can be read by several
    // threads: each write is atomic, and a name found by reference is always the name of its column.
    private readonly string?[] _foundNames;
    private int _nextIndex;

    internal CsvHeader(string[] columnNames, IEqualityComparer<string> comparer)
    {
        _columnNames = columnNames;
        _comparer = comparer;
        _foundNames = new string?[columnNames.Length];
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

        // Most callers read the columns in the same order for each record, with the same instances of the names: the column after the last one
        // found is checked first, by reference. A name that is the instance of a name found for a column is equal to its name with any comparer.
        var foundNames = _foundNames;
        var next = _nextIndex;
        if ((uint)next < (uint)foundNames.Length && ReferenceEquals(foundNames[next], columnName))
        {
            index = next;
            _nextIndex = next + 1 == foundNames.Length ? 0 : next + 1;
            return true;
        }

        if (!TryFindIndex(columnName, out index))
            return false;

        // A reference is stored only when it changes: storing it costs more than comparing it
        if (!ReferenceEquals(foundNames[index], columnName))
        {
            foundNames[index] = columnName;
        }

        _nextIndex = index + 1 == foundNames.Length ? 0 : index + 1;
        return true;
    }

    private bool TryFindIndex(string columnName, out int index)
    {
        if (_indices is not null)
            return _indices.TryGetValue(columnName, out index);

        // The columns read in another order: the names found are compared by reference before the names of the columns
        var foundNames = _foundNames;
        for (var i = 0; i < foundNames.Length; i++)
        {
            if (ReferenceEquals(foundNames[i], columnName))
            {
                index = i;
                return true;
            }
        }

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
