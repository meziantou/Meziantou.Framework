using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Represents a named query root exposed to SQL text queries.</summary>
public sealed class TdsQueryRoot
{
    private readonly Func<TdsQueryContext, IQueryable> _queryFactory;

    /// <summary>Initializes a new instance of the <see cref="TdsQueryRoot"/> class.</summary>
    public TdsQueryRoot(string name, Func<TdsQueryContext, IQueryable> queryFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(queryFactory);

        Name = name;
        _queryFactory = queryFactory;
    }

    /// <summary>Gets the SQL table name.</summary>
    public string Name { get; }

    internal TdsQueryRoot(string name, IQueryable query)
        : this(name, _ => query)
    {
    }

    internal IQueryable GetQuery(TdsQueryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return _queryFactory(context);
    }
}
