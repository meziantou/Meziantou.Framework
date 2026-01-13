namespace Meziantou.Framework.Csv;

/// <summary>Represents a column in a CSV file with its name and index position.</summary>
public class CsvColumn
{
    /// <summary>Initializes a new instance of the <see cref="CsvColumn"/> class with the specified index.</summary>
    /// <param name="index">The zero-based index of the column.</param>
    public CsvColumn(int index)
    {
        Index = index;
    }

    /// <summary>Initializes a new instance of the <see cref="CsvColumn"/> class with the specified name and index.</summary>
    /// <param name="name">The name of the column.</param>
    /// <param name="index">The zero-based index of the column.</param>
    public CsvColumn(string? name, int index)
    {
        Index = index;
        Name = name;
    }

    /// <summary>Gets the name of the column, or <see langword="null"/> if the CSV file has no header row.</summary>
    public string? Name { get; }

    /// <summary>Gets the zero-based index of the column.</summary>
    public int Index { get; }
}
