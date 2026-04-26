namespace Meziantou.Framework.Tds.Handler;

/// <summary>Describes a result-set column.</summary>
public sealed class TdsColumn
{
    /// <summary>Initializes a new instance of the <see cref="TdsColumn"/> class.</summary>
    public TdsColumn(string name, TdsColumnType columnType, bool isNullable = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        ColumnType = columnType;
        IsNullable = isNullable;
    }

    /// <summary>Gets the column name.</summary>
    public string Name { get; }

    /// <summary>Gets the column type.</summary>
    public TdsColumnType ColumnType { get; }

    /// <summary>Gets a value indicating whether the column can be null.</summary>
    public bool IsNullable { get; }
}
