using System.Collections;

namespace Meziantou.Framework.Csv;

/// <summary>Represents a row in a CSV file with access to values by column index or name.</summary>
/// <example>
/// <code>
/// using var reader = new StringReader("Name,Age\nJohn,30");
/// var csvReader = new CsvReader(reader) { HasHeaderRow = true };
/// var row = await csvReader.ReadRowAsync();
/// 
/// // Access by column name
/// Console.WriteLine(row["Name"]); // John
/// 
/// // Access by column index
/// Console.WriteLine(row[1]); // 30
/// </code>
/// </example>
public class CsvRow : IReadOnlyDictionary<string, string?>
{
    /// <summary>Gets the columns of the row, or <c>null</c> if the CSV file has no header row.</summary>
    public IReadOnlyList<CsvColumn>? Columns { get; }

    /// <summary>Gets the values of the row.</summary>
    public IReadOnlyList<string> Values { get; }

    internal CsvRow(IReadOnlyList<CsvColumn>? columns, IReadOnlyList<string> values)
    {
        Values = values;
        Columns = columns;
    }

    /// <summary>Gets the value at the specified column index.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    /// <returns>The value at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 0 or greater than or equal to the number of values.</exception>
    public virtual string? this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Values.Count);
            return Values[index];
        }
    }

    /// <summary>Gets the value of the column with the specified name.</summary>
    /// <param name="columnName">The name of the column.</param>
    /// <returns>The value of the column, or <c>null</c> if the column is not found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="columnName"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The CSV file has no header row.</exception>
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

    /// <summary>Gets the value of the specified column.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The value of the column.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="column"/> is <c>null</c>.</exception>
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
