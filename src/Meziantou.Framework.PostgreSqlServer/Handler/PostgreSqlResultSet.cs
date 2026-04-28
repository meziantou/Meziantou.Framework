using System.Collections.ObjectModel;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents a query result set.</summary>
public sealed class PostgreSqlResultSet
{
    /// <summary>Gets the column definitions.</summary>
    public Collection<PostgreSqlColumn> Columns { get; } = [];

    /// <summary>Gets the rows.</summary>
    public Collection<IReadOnlyList<object?>> Rows { get; } = [];
}
