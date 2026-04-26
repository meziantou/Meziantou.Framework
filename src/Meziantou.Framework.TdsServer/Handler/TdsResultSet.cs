using System.Collections.ObjectModel;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents a query result set.</summary>
public sealed class TdsResultSet
{
    /// <summary>Gets the column definitions.</summary>
    public Collection<TdsColumn> Columns { get; } = [];

    /// <summary>Gets the rows.</summary>
    public Collection<IReadOnlyList<object?>> Rows { get; } = [];
}
