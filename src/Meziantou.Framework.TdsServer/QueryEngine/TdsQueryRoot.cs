namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Represents a named query root exposed to SQL text queries.</summary>
public sealed class TdsQueryRoot
{
    /// <summary>Initializes a new instance of the <see cref="TdsQueryRoot"/> class.</summary>
    public TdsQueryRoot(string name, IQueryable query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(query);

        Name = name;
        Query = query;
    }

    /// <summary>Gets the SQL table name.</summary>
    public string Name { get; }

    /// <summary>Gets the typed query root.</summary>
    public IQueryable Query { get; }

    internal Type ElementType => Query.ElementType;
}
