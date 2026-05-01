using System.Collections.ObjectModel;

namespace Meziantou.Framework.Tds.QueryEngine;

/// <summary>Configures the built-in TDS query engine.</summary>
public sealed class TdsQueryEngineOptions
{
    /// <summary>Gets the stored procedures available to RPC requests.</summary>
    public IDictionary<string, Delegate> StoredProcedures { get; } = new Dictionary<string, Delegate>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the query roots available to SQL text queries.</summary>
    public Collection<TdsQueryRoot> QueryRoots { get; } = [];

    /// <summary>Gets or sets the materializer used to enumerate translated text queries.</summary>
    public TdsQueryMaterializer MaterializeAsync { get; set; } = DefaultMaterializeAsync;

    /// <summary>Gets the scalar SQL function mappings used by the query translator.</summary>
    public IDictionary<string, TdsQueryScalarFunction> ScalarFunctions { get; } = SqlFunctions.CreateDefaultScalarFunctions();

    /// <summary>Adds an <see cref="IQueryable{T}"/> query root.</summary>
    public TdsQueryEngineOptions AddQueryRoot<T>(string name, IQueryable<T> query)
    {
        QueryRoots.Add(new TdsQueryRoot(name, query));
        return this;
    }

    /// <summary>Adds an in-memory collection query root.</summary>
    public TdsQueryEngineOptions AddQueryRoot<T>(string name, IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        return AddQueryRoot(name, collection.AsQueryable());
    }

    /// <summary>Adds or replaces a scalar SQL function mapping.</summary>
    public TdsQueryEngineOptions AddScalarFunction(string name, TdsQueryScalarFunction function)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);

        ScalarFunctions[name] = function;
        return this;
    }

    private static ValueTask<IReadOnlyList<object?>> DefaultMaterializeAsync(IQueryable query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = new List<object?>();
        foreach (var row in query)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(row);
        }

        return ValueTask.FromResult<IReadOnlyList<object?>>(rows);
    }
}
