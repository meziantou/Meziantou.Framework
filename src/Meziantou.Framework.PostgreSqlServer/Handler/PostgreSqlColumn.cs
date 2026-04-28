namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Describes a result-set column.</summary>
public sealed class PostgreSqlColumn
{
    /// <summary>Initializes a new instance of the <see cref="PostgreSqlColumn"/> class.</summary>
    public PostgreSqlColumn(string name, PostgreSqlColumnType columnType, bool isNullable = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
        ColumnType = columnType;
        IsNullable = isNullable;
    }

    /// <summary>Gets the column name.</summary>
    public string Name { get; }

    /// <summary>Gets the column type.</summary>
    public PostgreSqlColumnType ColumnType { get; }

    /// <summary>Gets a value indicating whether the column can be null.</summary>
    public bool IsNullable { get; }
}
