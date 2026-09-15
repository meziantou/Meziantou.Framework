using System.Linq.Expressions;
using System.Reflection;
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
    private readonly TimeProvider _timeProvider;
    private FreeTextExpressionHandler<T>? _freeTextHandler;
    private UnhandledPropertyExpressionHandler<T>? _unhandledPropertyHandler;

    /// <summary>Initializes a new instance of <see cref="ExpressionQueryBuilder{T}"/> using the system time provider.</summary>
    public ExpressionQueryBuilder()
        : this(timeProvider: null)
    {
    }

    /// <summary>Initializes a new instance of <see cref="ExpressionQueryBuilder{T}"/> with a specific time provider for date-related range keywords.</summary>
    /// <param name="timeProvider">The time provider to use, or <see langword="null"/> to use <see cref="TimeProvider.System"/>.</param>
    public ExpressionQueryBuilder(TimeProvider? timeProvider)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private void AddHandlerCore(string key, KeyValueOperator op, Func<string, Expression<Func<T, bool>>> handler)
    {
        _handlers.Add(new ExpressionFilterKey(key.ToLowerInvariant(), op), handler);
    }

    /// <summary>Registers a handler for a string property using a "contains" comparison.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="selector">Expression selecting the string property.</param>
    /// <param name="comparisonType">
    /// The string comparison to use, or <see langword="null"/> to emit a plain <see cref="string.Contains(string)"/> call.
    /// Most LINQ providers, including Entity Framework Core, cannot translate the <see cref="StringComparison"/> overload
    /// of <see cref="string.Contains(string, StringComparison)"/>, so passing a value here makes the query usable in memory only.
    /// When left <see langword="null"/>, case sensitivity is decided by the underlying provider — the database collation for EF Core,
    /// and an ordinal comparison for LINQ to Objects.
    /// </param>
    public void AddHandler(string key, Expression<Func<T, string?>> selector, StringComparison? comparisonType = null)
    {
        Expression<Func<T, bool>> CreatePredicate(string value)
        {
            var box = new QueryValueStore<string>(value);
            var valueExpression = Expression.PropertyOrField(Expression.Constant(box), nameof(QueryValueStore<string>.Value));

            Expression body;
            if (comparisonType is null)
            {
                var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
                body = Expression.Call(selector.Body, containsMethod, valueExpression);
            }
            else
            {
                var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string), typeof(StringComparison)])!;
                body = Expression.Call(selector.Body, containsMethod, valueExpression, Expression.Constant(comparisonType.Value));
            }

            // The selected property is nullable, so guard the call. EF Core folds this into the generated SQL,
            // and in memory it stops a null property from throwing.
            var notNull = Expression.NotEqual(selector.Body, Expression.Constant(value: null, typeof(string)));

            return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(notNull, body), selector.Parameters);
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
        var parser = RangeSyntax.WithRelativeDates<TValue>(tryParseValue ?? ValueConverter.TryParseValue, _timeProvider);
        if (IsComparisonType<TValue>())
        {
            // Register range handler for equality (handles both simple equality and range syntax)
            AddHandlerCore(key, KeyValueOperator.EqualTo, value => CreateRangeExpression(value, selector, parser, _timeProvider));

            // Register comparison operators
            AddHandlerCore(key, KeyValueOperator.LessThan, value => CreateComparisonExpression(value, selector, Expression.LessThan, parser));
            AddHandlerCore(key, KeyValueOperator.LessThanOrEqual, value => CreateComparisonExpression(value, selector, Expression.LessThanOrEqual, parser));
            AddHandlerCore(key, KeyValueOperator.GreaterThan, value => CreateComparisonExpression(value, selector, Expression.GreaterThan, parser));
            AddHandlerCore(key, KeyValueOperator.GreaterThanOrEqual, value => CreateComparisonExpression(value, selector, Expression.GreaterThanOrEqual, parser));
        }
        else
        {
            // Just register equality
            AddHandlerCore(key, KeyValueOperator.EqualTo, value => CreateComparisonExpression(value, selector, Expression.Equal, parser));
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
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query))
            return new ExpressionQuery<T>(query, predicate: null);

        var syntax = QuerySyntax.Parse(query);
        var boundQuery = BoundQuery.Create(syntax);

        if (boundQuery is [[var single]])
            return new ExpressionQuery<T>(query, CreateExpression(single));

        // Combining the terms pairwise with AndAlso/OrElse would rebind the parameter of the whole expression built
        // so far at every step, which is quadratic. Instead, rebind each distinct term once onto a shared parameter.
        var parameter = Expression.Parameter(typeof(T), "item");
        var bodies = new Dictionary<BoundQuery, Expression>();
        var disjunctions = new Expression[boundQuery.Count];
        for (var i = 0; i < boundQuery.Count; i++)
        {
            var disjunction = boundQuery[i];
            var conjunctions = new Expression[disjunction.Count];
            for (var j = 0; j < disjunction.Count; j++)
            {
                // Distributing AND over OR repeats the same term in many disjunctions, so create its expression once
                var node = disjunction[j];
                if (!bodies.TryGetValue(node, out var body))
                {
                    var expression = CreateExpression(node);
                    body = new ReplaceParameterVisitor(expression.Parameters[0], parameter).Visit(expression.Body);
                    bodies.Add(node, body);
                }

                conjunctions[j] = body;
            }

            disjunctions[i] = Combine(conjunctions, Expression.AndAlso);
        }

        var filter = Expression.Lambda<Func<T, bool>>(Combine(disjunctions, Expression.OrElse), parameter);
        return new ExpressionQuery<T>(query, filter);
    }

    /// <summary>
    /// Combines the operands as a balanced tree, keeping their order. A left-leaning chain would be as deep as the
    /// number of operands, and compiling or translating the expression recurses through that depth.
    /// </summary>
    private static Expression Combine(ReadOnlySpan<Expression> operands, Func<Expression, Expression, BinaryExpression> combine)
    {
        if (operands.Length == 1)
            return operands[0];

        var half = operands.Length / 2;
        return combine(Combine(operands[..half], combine), Combine(operands[half..], combine));
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
        {
            // Free text is not supported, so it never matches, and its negation always does
            return CreateConstantExpression(node.IsNegated);
        }

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
        return CreateExpression(new BoundTextQuery(node.IsNegated, $"{node.Key}{op.ToQueryText()}{node.Value}"));
    }

    /// <summary>
    /// Determines whether <typeparamref name="TValue"/> can be used with <see cref="Expression.LessThan(Expression, Expression)"/>
    /// and friends, so the comparison operators are worth registering for it.
    /// </summary>
    private static bool IsComparisonType<TValue>()
    {
        // Unwrap Nullable<T>: the comparison operators are lifted, so an int? property is still orderable
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        // Enums are deliberately excluded: Expression.LessThan is not defined for an enum type, it needs both
        // operands converted to the underlying type first. They keep equality only, as before.
        if (type.IsEnum)
            return false;

        // The built-in numeric types implement < with an IL instruction, so there is no operator method to find
        if (type == typeof(sbyte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
            type == typeof(byte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong) ||
            type == typeof(nint) || type == typeof(nuint) || type == typeof(char) ||
            type == typeof(float) || type == typeof(double))
        {
            return true;
        }

        // Everything else that can be ordered exposes an operator method: decimal, DateTime, DateTimeOffset,
        // DateOnly, TimeOnly, TimeSpan, Half, Int128, UInt128, BigInteger, and any user-defined type.
        // Probing for it also keeps types that cannot be ordered, such as Guid, out of the comparison path
        // instead of letting Expression.LessThan throw when the handler is registered.
        return type.GetMethod("op_LessThan", BindingFlags.Public | BindingFlags.Static) is not null;
    }

    private static Expression<Func<T, bool>> CreateComparisonExpression<TValue>(
        string value,
        Expression<Func<T, TValue>> selector,
        Func<Expression, Expression, Expression> comparisonFactory,
        ScalarParser<TValue> parser)
    {
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
        ScalarParser<TValue> parser,
        TimeProvider timeProvider)
    {

        // Try to parse as range
        var range = RangeSyntax.TryParse(value, parser, timeProvider);
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

        // RangeSyntax.TryParse already tried the value as a single operand
        return CreateFalseExpression();
    }

    private static Expression<Func<T, bool>> CreateFalseExpression() => CreateConstantExpression(value: false);

    private static Expression<Func<T, bool>> CreateConstantExpression(bool value)
    {
        var parameter = Expression.Parameter(typeof(T), "item");
        return Expression.Lambda<Func<T, bool>>(Expression.Constant(value), parameter);
    }

    private sealed class ReplaceParameterVisitor(ParameterExpression oldParameter, ParameterExpression newParameter) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == oldParameter ? newParameter : base.VisitParameter(node);
        }
    }

    private readonly record struct ExpressionFilterKey(string Key, KeyValueOperator Operator);
}
