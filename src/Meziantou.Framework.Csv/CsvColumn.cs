namespace Meziantou.Framework.Csv;

/// <summary>
/// Represents a column in CSV data.
/// </summary>
public class CsvColumn
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CsvColumn"/> class with the specified index.
    /// </summary>
    /// <param name="index">The zero-based column index.</param>
    public CsvColumn(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvColumn"/> class with the specified name and index.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="index">The zero-based column index.</param>
    public CsvColumn(string? name, int index)
    {
        Index = index;
        Name = name;
    }

    /// <summary>
    /// Gets the column name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the zero-based column index.
    /// </summary>
    public int Index { get; }
}
