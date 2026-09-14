using System.Diagnostics;
using Meziantou.Framework.SimpleQueryLanguage.Binding;
using Meziantou.Framework.SimpleQueryLanguage.Ranges;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Builds query objects from query strings with custom property handlers.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <example>
/// <code>
/// var collection = new List&lt;Person&gt;();
/// var queryBuilder = new QueryBuilder&lt;Person&gt;();
/// queryBuilder.AddHandler&lt;string&gt;("name", (obj, value) => obj.FullName.Contains(value, StringComparison.OrdinalIgnoreCase));
/// queryBuilder.AddRangeHandler&lt;int&gt;("age", (obj, range) => range.IsInRange((int)(DateTime.UtcNow - obj.DateOfBirth).TotalDays / 365));
/// var query = queryBuilder.Build("name:john AND age>=21");
/// var result = query.Evaluate(new Person("John Doe", new DateTime(2000, 1, 1)));
/// </code>
/// </example>
public sealed class QueryBuilder<T>
{
    private static readonly Predicate<T> AlwaysFalsePredicate = _ => false;
    private static readonly Predicate<T> AlwaysTruePredicate = _ => true;

    private readonly TimeProvider _timeProvider;

    // Each handler turns the operator and value of a query term into a predicate when the query is built,
    // so values are parsed once per term instead of once per evaluated object.
    private readonly Dictionary<FilterKeyValue, Func<KeyValueOperator, string, Predicate<T>>> _filters = [];
    private Func<T, string, bool>? _freeTextFilter;
    private UnhandledPropertyDelegate<T>? _unhandledPropertyFilter;

    /// <summary>Initializes a new instance of <see cref="QueryBuilder{T}"/> using the system time provider.</summary>
    public QueryBuilder()
        : this(timeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="QueryBuilder{T}"/> with a specific time provider for date-related range keywords.</summary>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use <see cref="TimeProvider.System"/>.</param>
    public QueryBuilder(TimeProvider? timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private void RegisterHandler(string key, string? value, Func<KeyValueOperator, string, Predicate<T>> handler)
    {
        _filters.Add(new FilterKeyValue(key.ToLowerInvariant(), value?.ToLowerInvariant()), handler);
    }

    /// <summary>Registers a handler for a key-value pair that evaluates based on the operator.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="value">The specific value to match, or null to match any value.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler(string key, string? value, Func<T, KeyValueOperator, bool> predicate)
    {
        RegisterHandler(key, value, (op, _) => obj => predicate(obj, op));
    }

    /// <summary>Registers a handler for a property key.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler(string key, Func<T, KeyValueOperator, string, bool> predicate)
    {
        RegisterHandler(key, value: null, (op, value) => obj => predicate(obj, op, value));
    }

    /// <summary>Registers a handler for a property key with automatic value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler<TValue>(string key, Func<T, KeyValueOperator, TValue, bool> predicate)
    {
        AddHandler(key, predicate, tryParseValue: null);
    }

    /// <summary>Registers a handler for a property key with custom value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    /// <param name="tryParseValue">Custom parser for the value, or null to use the default parser.</param>
    public void AddHandler<TValue>(string key, Func<T, KeyValueOperator, TValue, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        var tryParse = tryParseValue ?? ValueConverter.TryParseValue;
        RegisterHandler(key, value: null, (op, value) =>
        {
            if (!tryParse(value, out var parsedValue))
                return AlwaysFalsePredicate;

            return obj => predicate(obj, op, parsedValue);
        });
    }

    /// <summary>Registers a handler for a specific key-value pair.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="value">The specific value to match, or null to match any value.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler(string key, string? value, Func<T, bool> predicate)
    {
        RegisterHandler(key, value, (op, _) => ApplyEqualOperator(op, obj => predicate(obj)));
    }

    /// <summary>Registers a handler for a property key with string value.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler(string key, Func<T, string, bool> predicate)
    {
        RegisterHandler(key, value: null, (op, value) => ApplyEqualOperator(op, obj => predicate(obj, value)));
    }

    /// <summary>Registers a handler for a property key with automatic value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    public void AddHandler<TValue>(string key, Func<T, TValue, bool> predicate)
    {
        AddHandler(key, predicate, tryParseValue: null);
    }

    /// <summary>Registers a handler for a property key with custom value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the query.</param>
    /// <param name="tryParseValue">Custom parser for the value, or null to use the default parser.</param>
    public void AddHandler<TValue>(string key, Func<T, TValue, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        var tryParse = tryParseValue ?? ValueConverter.TryParseValue;
        RegisterHandler(key, value: null, (op, value) =>
        {
            // The predicate only tests for a match, so only the equality operators have a meaning
            if (op is not (KeyValueOperator.EqualTo or KeyValueOperator.NotEqualTo) || !tryParse(value, out var parsedValue))
                return AlwaysFalsePredicate;

            return ApplyEqualOperator(op, obj => predicate(obj, parsedValue));
        });
    }

    /// <summary>Registers a range handler for a property key with custom value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the range query.</param>
    /// <param name="tryParseValue">Custom parser for the value, or null to use the default parser.</param>
    public void AddRangeHandler<TValue>(string key, Func<T, RangeSyntax<TValue>, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        var tryParse = tryParseValue ?? ValueConverter.TryParseValue;
        RegisterHandler(key, value: null, (op, value) => CreateRangePredicate(op, value, predicate, tryParse));
    }

    /// <summary>Registers a range handler for a property key with automatic value parsing.</summary>
    /// <typeparam name="TValue">The type to parse the value as.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="predicate">The function to evaluate the range query.</param>
    public void AddRangeHandler<TValue>(string key, Func<T, RangeSyntax<TValue>, bool> predicate)
    {
        AddRangeHandler(key, predicate, tryParseValue: null);
    }

    // Ranges
    private Predicate<T> CreateRangePredicate<TValue>(KeyValueOperator op, string value, Func<T, RangeSyntax<TValue>, bool> predicate, ScalarParser<TValue> tryParseValue)
    {
        // field:1..10
        // field=1..10
        // field<>1..10
        if (op is KeyValueOperator.EqualTo or KeyValueOperator.NotEqualTo)
        {
            var range = RangeSyntax.TryParse(value, tryParseValue, _timeProvider);
            if (range is null)
                return AlwaysFalsePredicate;

            return ApplyEqualOperator(op, obj => predicate(obj, range));
        }

        // field>=1
        if (tryParseValue(value, out var parsedValue))
        {
            var range = new UnaryRangeSyntax<TValue>(op, parsedValue);
            return obj => predicate(obj, range);
        }

        return AlwaysFalsePredicate;
    }

    private static Predicate<T> ApplyEqualOperator(KeyValueOperator op, Predicate<T> predicate)
    {
        return op switch
        {
            KeyValueOperator.EqualTo => predicate,
            KeyValueOperator.NotEqualTo => Negate(predicate),
            _ => AlwaysFalsePredicate, // Not supported
        };
    }

    private static Predicate<T> Negate(Predicate<T> predicate)
    {
        if (predicate == AlwaysFalsePredicate)
            return AlwaysTruePredicate;

        if (predicate == AlwaysTruePredicate)
            return AlwaysFalsePredicate;

        return obj => !predicate(obj);
    }

    /// <summary>Sets the handler for free-text search terms without a property key.</summary>
    /// <param name="predicate">The function to evaluate free-text queries.</param>
    public void SetTextFilterHandler(Func<T, string, bool> predicate)
    {
        _freeTextFilter = predicate;
    }

    /// <summary>Sets the handler for property keys that don't have a registered handler.</summary>
    /// <param name="predicate">The function to evaluate unhandled properties, or null to use default behavior.</param>
    public void SetUnhandledPropertyHandler(UnhandledPropertyDelegate<T>? predicate)
    {
        _unhandledPropertyFilter = predicate;
    }

    /// <summary>Builds a query from a query string.</summary>
    /// <param name="query">The query string to parse.</param>
    /// <returns>A compiled query that can be evaluated against objects.</returns>
    /// <remarks>
    /// Values, including date keywords such as <c>today</c>, are parsed when the query is built. Handlers registered
    /// or changed afterward do not affect the returned query.
    /// </remarks>
    public Query<T> Build(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var predicate = CreatePredicate(query);
        return new Query<T>(query, predicate);
    }

    private Predicate<T> CreatePredicate(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return AlwaysTruePredicate;

        var syntax = QuerySyntax.Parse(query);
        var boundQuery = BoundQuery.Create(syntax);

        var context = new BuildContext(_freeTextFilter, _unhandledPropertyFilter);
        var disjunctions = new Predicate<T>[boundQuery.Count][];
        for (var i = 0; i < boundQuery.Count; i++)
        {
            var disjunction = boundQuery[i];
            var conjunctions = new Predicate<T>[disjunction.Count];
            for (var j = 0; j < disjunction.Count; j++)
            {
                // Distributing AND over OR repeats the same term in many disjunctions, so create its predicate once
                var node = disjunction[j];
                if (!context.Predicates.TryGetValue(node, out var predicate))
                {
                    predicate = CreatePredicate(node, context);
                    context.Predicates.Add(node, predicate);
                }

                conjunctions[j] = predicate;
            }

            disjunctions[i] = conjunctions;
        }

        if (disjunctions is [[var single]])
            return single;

        // Evaluate with loops rather than nested delegates: a query can have thousands of disjunctions,
        // and a chain of delegates that deep overflows the stack.
        return obj => Evaluate(disjunctions, obj);
    }

    private static bool Evaluate(Predicate<T>[][] disjunctions, T value)
    {
        foreach (var conjunctions in disjunctions)
        {
            if (MatchesAll(conjunctions, value))
                return true;
        }

        return false;

        static bool MatchesAll(Predicate<T>[] predicates, T value)
        {
            foreach (var predicate in predicates)
            {
                if (!predicate(value))
                    return false;
            }

            return true;
        }
    }

    private Predicate<T> CreatePredicate(BoundQuery node, BuildContext context)
    {
        return node switch
        {
            BoundTextQuery textQuery => CreatePredicate(textQuery, context),
            BoundKeyValueQuery keyValueQuery => CreatePredicate(keyValueQuery, context),
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unexpected node: {node.GetType()}"),
        };
    }

    private static Predicate<T> CreatePredicate(BoundTextQuery node, BuildContext context)
    {
        var freeTextFilter = context.FreeTextFilter;
        if (freeTextFilter is null)
        {
            // Free text is not supported, so it never matches, and its negation always does
            return node.IsNegated ? AlwaysTruePredicate : AlwaysFalsePredicate;
        }

        var text = node.Text;
        return node.IsNegated
                ? obj => !freeTextFilter(obj, text)
                : obj => freeTextFilter(obj, text);
    }

    private Predicate<T> CreatePredicate(BoundKeyValueQuery node, BuildContext context)
    {
        var key = node.Key.ToLowerInvariant();
        var value = node.Value.ToLowerInvariant();

        if (_filters.TryGetValue(new FilterKeyValue(key, value), out var handler) ||
            _filters.TryGetValue(new FilterKeyValue(key, value: null), out handler))
        {
            var predicate = handler(node.Operator, node.Value);
            return node.IsNegated ? Negate(predicate) : predicate;
        }

        var unhandledPropertyFilter = context.UnhandledPropertyFilter;
        if (unhandledPropertyFilter is not null)
        {
            var (propertyName, op, propertyValue) = (node.Key, node.Operator, node.Value);
            return node.IsNegated
                    ? obj => !unhandledPropertyFilter(obj, propertyName, op, propertyValue)
                    : obj => unhandledPropertyFilter(obj, propertyName, op, propertyValue);
        }

        return CreatePredicate(new BoundTextQuery(node.IsNegated, $"{node.Key}{node.Operator.ToQueryText()}{node.Value}"), context);
    }

    /// <summary>The handlers as they were when the query was built, and the predicates created so far.</summary>
    private sealed class BuildContext(Func<T, string, bool>? freeTextFilter, UnhandledPropertyDelegate<T>? unhandledPropertyFilter)
    {
        public Func<T, string, bool>? FreeTextFilter { get; } = freeTextFilter;
        public UnhandledPropertyDelegate<T>? UnhandledPropertyFilter { get; } = unhandledPropertyFilter;
        public Dictionary<BoundQuery, Predicate<T>> Predicates { get; } = [];
    }

    private readonly record struct FilterKeyValue
    {
        [SuppressMessage("Performance", "CA1862:Prefer the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "Validation")]
        public FilterKeyValue(string key, string? value)
        {
            ArgumentNullException.ThrowIfNull(key);

            Debug.Assert(key.ToLowerInvariant() == key);
            Debug.Assert(value?.ToLowerInvariant() == value);

            Key = key;
            Value = value;
        }

        public string Key { get; }
        public string? Value { get; }
    }
}
