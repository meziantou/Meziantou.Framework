using System.Linq.Expressions;
using Meziantou.Framework.SimpleQueryLanguage.Binding;
using Meziantou.Framework.SimpleQueryLanguage.Ranges;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage;

/// <summary>Builds expression-based queries from query strings for use with IQueryable and EF Core.</summary>
/// <typeparam name="T">The type of object to query against.</typeparam>
/// <remarks>
/// Unlike <see cref="QueryBuilder{T}"/> which uses <see cref="Predicate{T}"/> for in-memory evaluation,
/// this builder creates <see cref="Expression{TDelegate}"/> objects that can be translated to SQL by EF Core.
/// </remarks>
/// <example>
/// <code>
/// var queryBuilder = new ExpressionQueryBuilder&lt;Person&gt;();
/// queryBuilder.AddHandler("name", item => item.FullName);
/// queryBuilder.AddHandler&lt;int&gt;("age", item => item.Age);
/// var query = queryBuilder.Build("name:john AND age>=21");
/// var results = await dbContext.People.Apply(query).ToListAsync();
/// </code>
/// </example>
public sealed class ExpressionQueryBuilder<T>
{
    private readonly Dictionary<ExpressionFilterKey, Func<string, Expression<Func<T, bool>>>> _handlers = [];
    private FreeTextExpressionHandler<T>? _freeTextHandler;
    private UnhandledPropertyExpressionHandler<T>? _unhandledPropertyHandler;

    private void AddHandlerCore(string key, KeyValueOperator op, Func<string, Expression<Func<T, bool>>> handler)
    {
        _handlers.Add(new ExpressionFilterKey(key.ToLowerInvariant(), op), handler);
    }

    /// <summary>Registers a handler for a string property with equality comparison.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="selector">Expression selecting the string property.</param>
    /// <param name="comparisonType">The string comparison type to use.</param>
    public void AddHandler(string key, Expression<Func<T, string?>> selector, StringComparison comparisonType = StringComparison.OrdinalIgnoreCase)
    {
        Expression<Func<T, bool>> CreatePredicate(string value)
        {
            var box = new QueryValueStore<string>(value);
            var valueExpression = Expression.PropertyOrField(Expression.Constant(box), nameof(QueryValueStore<string>.Value));

            var comparisonExpression = Expression.Constant(comparisonType);
            var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string), typeof(StringComparison)])!;
            var body = Expression.Call(selector.Body, containsMethod, valueExpression, comparisonExpression);

            return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
        }

        AddHandlerCore(key, KeyValueOperator.EqualTo, CreatePredicate);
    }

    /// <summary>Registers a handler for a property with automatic comparison operators.</summary>
    /// <typeparam name="TValue">The type of the property.</typeparam>
    /// <param name="key">The property key to handle.</param>
    /// <param name="selector">Expression selecting the property.</param>
    /// <param name="tryParseValue">Custom parser for the value, or null to use the default parser.</param>
    public void AddHandler<TValue>(string key, Expression<Func<T, TValue>> selector, ScalarParser<TValue>? tryParseValue = null)
    {
        if (IsComparisonType<TValue>())
        {
            // Register range handler for equality (handles both simple equality and range syntax)
            AddHandlerCore(key, KeyValueOperator.EqualTo, value => CreateRangeExpression(value, selector, tryParseValue));

            // Register comparison operators
            AddHandlerCore(key, KeyValueOperator.LessThan, value => CreateComparisonExpression(value, selector, Expression.LessThan, tryParseValue));
            AddHandlerCore(key, KeyValueOperator.LessThanOrEqual, value => CreateComparisonExpression(value, selector, Expression.LessThanOrEqual, tryParseValue));
            AddHandlerCore(key, KeyValueOperator.GreaterThan, value => CreateComparisonExpression(value, selector, Expression.GreaterThan, tryParseValue));
            AddHandlerCore(key, KeyValueOperator.GreaterThanOrEqual, value => CreateComparisonExpression(value, selector, Expression.GreaterThanOrEqual, tryParseValue));
        }
        else
        {
            // Just register equality
            AddHandlerCore(key, KeyValueOperator.EqualTo, value => CreateComparisonExpression(value, selector, Expression.Equal, tryParseValue));
        }
    }

    /// <summary>Registers a custom expression handler for a property key and operator.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="op">The operator to handle.</param>
    /// <param name="handler">Function that creates the filter expression from the query value.</param>
    public void AddHandler(string key, KeyValueOperator op, Func<string, Expression<Func<T, bool>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[new ExpressionFilterKey(key.ToLowerInvariant(), op)] = handler;
    }

    /// <summary>Registers a custom expression handler for a property key with equality operator.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="handler">Function that creates the filter expression from the query value.</param>
    public void AddHandler(string key, Func<string, Expression<Func<T, bool>>> handler)
    {
        AddHandler(key, KeyValueOperator.EqualTo, handler);
    }

    /// <summary>Sets the handler for free-text search terms without a property key.</summary>
    /// <param name="handler">The function to create filter expressions for free-text queries.</param>
    public void SetFreeTextHandler(FreeTextExpressionHandler<T>? handler)
    {
        _freeTextHandler = handler;
    }

    /// <summary>Sets the handler for property keys that don't have a registered handler.</summary>
    /// <param name="handler">The function to create filter expressions for unhandled properties.</param>
    public void SetUnhandledPropertyHandler(UnhandledPropertyExpressionHandler<T>? handler)
    {
        _unhandledPropertyHandler = handler;
    }

    /// <summary>Builds a query from a query string.</summary>
    /// <param name="query">The query string to parse.</param>
    /// <returns>A compiled expression query that can be applied to an <see cref="IQueryable{T}"/>.</returns>
    public ExpressionQuery<T> Build(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new ExpressionQuery<T>(query, predicate: null);

        var syntax = QuerySyntax.Parse(query);
        var boundQuery = BoundQuery.Create(syntax);

        Expression<Func<T, bool>>? filter = null;

        foreach (var disjunction in boundQuery)
        {
            Expression<Func<T, bool>>? disjunctionPredicate = null;

            foreach (var conjunction in disjunction)
            {
                var next = CreateExpression(conjunction);
                disjunctionPredicate = disjunctionPredicate is null
                    ? next
                    : disjunctionPredicate.AndAlso(next);
            }

            if (disjunctionPredicate is not null)
            {
                filter = filter is null
                    ? disjunctionPredicate
                    : filter.OrElse(disjunctionPredicate);
            }
        }

        return new ExpressionQuery<T>(query, filter);
    }

    private Expression<Func<T, bool>> CreateExpression(BoundQuery node)
    {
        return node switch
        {
            BoundTextQuery textQuery => CreateExpression(textQuery),
            BoundKeyValueQuery keyValueQuery => CreateExpression(keyValueQuery),
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unexpected node: {node.GetType()}"),
        };
    }

    private Expression<Func<T, bool>> CreateExpression(BoundTextQuery node)
    {
        if (_freeTextHandler is null)
            return CreateFalseExpression();

        var expression = _freeTextHandler(node.Text);
        return node.IsNegated ? expression.Negate() : expression;
    }

    private Expression<Func<T, bool>> CreateExpression(BoundKeyValueQuery node)
    {
        var key = node.Key.ToLowerInvariant();
        var value = node.Value;
        var op = node.Operator;

        // Try to find a handler for the exact operator
        if (_handlers.TryGetValue(new ExpressionFilterKey(key, op), out var handler))
        {
            var expression = handler(value);
            return node.IsNegated ? expression.Negate() : expression;
        }

        // For NotEqualTo, try to find EqualTo handler and negate
        if (op == KeyValueOperator.NotEqualTo && _handlers.TryGetValue(new ExpressionFilterKey(key, KeyValueOperator.EqualTo), out handler))
        {
            var expression = handler(value);
            return node.IsNegated ? expression : expression.Negate();
        }

        // Try unhandled property handler
        if (_unhandledPropertyHandler is not null)
        {
            var expression = _unhandledPropertyHandler(node.Key, op, value);
            if (expression is not null)
                return node.IsNegated ? expression.Negate() : expression;
        }

        // Fall back to free text
        return CreateExpression(new BoundTextQuery(node.IsNegated, $"{node.Key}:{node.Value}"));
    }

    private static bool IsComparisonType<TValue>()
    {
        return typeof(TValue) == typeof(sbyte) || typeof(TValue) == typeof(short) || typeof(TValue) == typeof(int) || typeof(TValue) == typeof(long) ||
               typeof(TValue) == typeof(byte) || typeof(TValue) == typeof(ushort) || typeof(TValue) == typeof(uint) || typeof(TValue) == typeof(ulong) ||
#if NET7_0_OR_GREATER
               typeof(TValue) == typeof(Int128) || typeof(TValue) == typeof(UInt128) ||
#endif
               typeof(TValue) == typeof(float) || typeof(TValue) == typeof(double) || typeof(TValue) == typeof(decimal) ||
               typeof(TValue) == typeof(DateTime) || typeof(TValue) == typeof(DateTimeOffset) || typeof(TValue) == typeof(DateOnly) || typeof(TValue) == typeof(TimeOnly);
    }

    private static Expression<Func<T, bool>> CreateComparisonExpression<TValue>(
        string value,
        Expression<Func<T, TValue>> selector,
        Func<Expression, Expression, Expression> comparisonFactory,
        ScalarParser<TValue>? tryParseValue)
    {
        var parser = tryParseValue ?? ValueConverter.TryParseValue;
        if (!parser(value, out var parsedValue))
            return CreateFalseExpression();

        return CreateComparisonExpressionCore(parsedValue, selector, comparisonFactory);
    }

    private static Expression<Func<T, bool>> CreateComparisonExpressionCore<TValue>(
        TValue value,
        Expression<Func<T, TValue>> selector,
        Func<Expression, Expression, Expression> comparisonFactory)
    {
        var box = new QueryValueStore<TValue>(value);
        var valueExpression = Expression.PropertyOrField(Expression.Constant(box), nameof(QueryValueStore<TValue>.Value));

        var body = comparisonFactory(selector.Body, valueExpression);
        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    private static Expression<Func<T, bool>> CreateRangeExpression<TValue>(
        string value,
        Expression<Func<T, TValue>> selector,
        ScalarParser<TValue>? tryParseValue)
    {
        var parser = tryParseValue ?? ValueConverter.TryParseValue;

        // Try to parse as range
        var range = RangeSyntax.TryParse(value, parser);
        if (range is BinaryRangeSyntax<TValue> binary)
        {
            var lowerExpression = CreateComparisonExpressionCore(
                binary.LowerBound,
                selector,
                binary.LowerBoundIncluded ? Expression.GreaterThanOrEqual : Expression.GreaterThan);

            var upperExpression = CreateComparisonExpressionCore(
                binary.UpperBound,
                selector,
                binary.UpperBoundIncluded ? Expression.LessThanOrEqual : Expression.LessThan);

            return lowerExpression.AndAlso(upperExpression);
        }

        if (range is UnaryRangeSyntax<TValue> unary)
        {
            Func<Expression, Expression, Expression> factory = unary.Operator switch
            {
                KeyValueOperator.LessThan => Expression.LessThan,
                KeyValueOperator.LessThanOrEqual => Expression.LessThanOrEqual,
                KeyValueOperator.GreaterThan => Expression.GreaterThan,
                KeyValueOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual,
                _ => Expression.Equal,
            };

            return CreateComparisonExpressionCore(unary.Operand, selector, factory);
        }

        // Fall back to simple equality
        return CreateComparisonExpression(value, selector, Expression.Equal, parser);
    }

    private static Expression<Func<T, bool>> CreateFalseExpression()
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        return Expression.Lambda<Func<T, bool>>(Expression.Constant(false), parameter);
    }

    private readonly record struct ExpressionFilterKey(string Key, KeyValueOperator Operator);
}
