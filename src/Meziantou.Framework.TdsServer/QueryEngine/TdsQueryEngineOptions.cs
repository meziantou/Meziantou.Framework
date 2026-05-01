using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;

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
    public IDictionary<string, TdsQueryScalarFunction> ScalarFunctions { get; } = CreateDefaultScalarFunctions();

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

    private static IDictionary<string, TdsQueryScalarFunction> CreateDefaultScalarFunctions()
    {
        return new Dictionary<string, TdsQueryScalarFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["UPPER"] = BuildUpperInvariantFunction,
            ["LOWER"] = BuildLowerInvariantFunction,
            ["LEN"] = BuildLenFunction,
            ["CONCAT"] = BuildConcatFunction,
        };
    }

    private static Expression BuildUpperInvariantFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "UPPER");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.ToUpperInvariant), Type.EmptyTypes)!);
    }

    private static Expression BuildLowerInvariantFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LOWER");
        return Expression.Call(EnsureString(arguments[0]), typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
    }

    private static Expression BuildLenFunction(IReadOnlyList<Expression> arguments)
    {
        ValidateArgCount(arguments, expectedCount: 1, "LEN");
        return Expression.Property(EnsureString(arguments[0]), nameof(string.Length));
    }

    private static Expression BuildConcatFunction(IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count == 0)
        {
            throw new TdsQueryEngineException("CONCAT requires at least one argument.");
        }

        if (arguments.Count == 1)
        {
            return EnsureString(arguments[0]);
        }

        var stringArguments = arguments.Select(EnsureString);
        return Expression.Call(typeof(string).GetMethod(nameof(string.Concat), [typeof(string[])])!, Expression.NewArrayInit(typeof(string), stringArguments));
    }

    private static void ValidateArgCount(IReadOnlyList<Expression> arguments, int expectedCount, string functionName)
    {
        if (arguments.Count != expectedCount)
        {
            throw new TdsQueryEngineException($"{functionName} requires exactly {expectedCount.ToString(CultureInfo.InvariantCulture)} argument.");
        }
    }

    private static Expression EnsureString(Expression expression)
    {
        if (expression.Type == typeof(string))
        {
            return expression;
        }

        return Expression.Call(
            typeof(Convert).GetMethod(nameof(Convert.ToString), [typeof(object), typeof(IFormatProvider)])!,
            Expression.Convert(expression, typeof(object)),
            Expression.Constant(CultureInfo.InvariantCulture));
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
