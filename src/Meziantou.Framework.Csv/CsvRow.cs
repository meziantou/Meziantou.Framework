using System.Collections;

namespace Meziantou.Framework.Csv;

/// <summary>
/// Represents a row in a CSV file.
/// </summary>
public class CsvRow : IReadOnlyDictionary<string, string?>
{
    /// <summary>
    /// Gets the columns for this row.
    /// </summary>
    public IReadOnlyList<CsvColumn>? Columns { get; }

    /// <summary>
    /// Gets the values for this row.
    /// </summary>
    public IReadOnlyList<string> Values { get; }

    internal CsvRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
    {
        Values = values;
        Columns = columns;
    }

    /// <summary>
    /// Gets the value at the specified column index.
    /// </summary>
    /// <param name="index">The zero-based index of the column.</param>
    /// <returns>The value at the specified index.</returns>
    public virtual string? this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Values.Count);
            return Values[index];
        }
    }

    /// <summary>
    /// Gets the value in the column with the specified name.
    /// </summary>
    /// <param name="columnName">The name of the column.</param>
    /// <returns>The value in the specified column, or <see langword="null"/> if the column is not found.</returns>
    public virtual string? this[string columnName]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(columnName);

            if (Columns is null)
                throw new InvalidOperationException("Columns are not parsed");

            var column = Columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.Ordinal));
            if (column is null)
                return null;

            return this[column];
        }
    }

    /// <summary>
    /// Gets the value in the specified column.
    /// </summary>
    /// <param name="column">The column to get the value from.</param>
    /// <returns>The value in the specified column.</returns>
    public virtual string? this[CsvColumn column]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(column);

            return this[column.Index];
        }
    }

    IEnumerable<string> IReadOnlyDictionary<string, string?>.Keys => Columns is null ? Enumerable.Empty<string>() : Columns.Where(c => c.Name is not null).Select(c => c.Name!);

    IEnumerable<string> IReadOnlyDictionary<string, string?>.Values => Values;

    int IReadOnlyCollection<KeyValuePair<string, string?>>.Count => Values.Count;

    string? IReadOnlyDictionary<string, string?>.this[string key] => this[key];

    bool IReadOnlyDictionary<string, string?>.ContainsKey(string key)
    {
        if (Columns is null)
            return false;

        return Columns.Any(c => string.Equals(c.Name, key, StringComparison.Ordinal));
    }

    bool IReadOnlyDictionary<string, string?>.TryGetValue(string key, out string? value)
    {
        var v = this[key];
        if (v is not null)
        {
            value = v;
            return true;
        }

        value = default;
        return false;
    }

    IEnumerator<KeyValuePair<string, string?>> IEnumerable<KeyValuePair<string, string?>>.GetEnumerator()
    {
        if (Columns is null)
            return Values.Select(v => new KeyValuePair<string, string?>(key: "", v)).GetEnumerator();

        return Columns.Select(c => new KeyValuePair<string, string?>(c.Name ?? "", this[c])).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return Values.GetEnumerator();
    }
}
