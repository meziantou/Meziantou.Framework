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
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.SimpleQueryLanguage.Tests/ExpressionQueryBuilderTests.cs" ref:region="ExpressionQueryBuilder_Build" ref:dedent="true">
/// var queryBuilder = new ExpressionQueryBuilder&lt;Person&gt;();
/// queryBuilder.AddHandler("name", item =&gt; item.FullName);
/// queryBuilder.AddHandler&lt;int&gt;("age", item =&gt; item.Age);
/// var query = queryBuilder.Build("name:John AND age&gt;=21");
///
/// // Apply works on any IQueryable&lt;T&gt;, such as an Entity Framework Core DbSet&lt;T&gt;
/// var people = new[] { new Person("John Doe", 30), new Person("John Smith", 18), new Person("Jane Doe", 25) }.AsQueryable();
/// var results = query.Apply(people).ToList();
/// Assert.Equal("John Doe", Assert.Single(results).FullName);
/// </code>
/// </example>
public sealed class ExpressionQueryBuilder<T>
{
    private readonly Dictionary<ExpressionFilterKey, Func<string, Expression<Func<T, bool>>>> _handlers = [];
    private readonly HashSet<string> _keys = [];
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
        var lowerKey = key.ToLowerInvariant();
        _handlers.Add(new ExpressionFilterKey(lowerKey, op), handler);
        _keys.Add(lowerKey);
    }

    /// <summary>Gets or sets whether <see cref="Build"/> throws when the query has diagnostics.</summary>
    /// <value>
    /// <see langword="true"/> to throw an <see cref="InvalidQueryException"/> for a query with diagnostics, such as an unbalanced
    /// parenthesis or an unknown key; <see langword="false"/> (the default) to read the query leniently and report the
    /// diagnostics in <see cref="ExpressionQuery{T}.Diagnostics"/>.
    /// </value>
    public bool Strict { get; set; }

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
        AddHandler(key, selector, StringMatchMode.Contains, comparisonType);
    }

    /// <summary>Registers a handler for a string property using the specified match mode.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="selector">Expression selecting the string property.</param>
    /// <param name="matchMode">How the property matches the value of the query.</param>
    /// <param name="comparisonType">
    /// The string comparison to use, or <see langword="null"/> to emit the overload without a <see cref="StringComparison"/>
    /// (or the <c>==</c> operator for <see cref="StringMatchMode.Exact"/>).
    /// Most LINQ providers, including Entity Framework Core, cannot translate the <see cref="StringComparison"/> overloads,
    /// so passing a value here makes the query usable in memory only.
    /// When left <see langword="null"/>, case sensitivity is decided by the underlying provider — the database collation for EF Core,
    /// and an ordinal comparison for LINQ to Objects.
    /// </param>
    public void AddHandler(string key, Expression<Func<T, string?>> selector, StringMatchMode matchMode, StringComparison? comparisonType = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (!Enum.IsDefined(matchMode))
            throw new ArgumentOutOfRangeException(nameof(matchMode), matchMode, message: null);

        Expression<Func<T, bool>> CreatePredicate(string value)
        {
            var box = new QueryValueStore<string>(value);
            var valueExpression = Expression.PropertyOrField(Expression.Constant(box), nameof(QueryValueStore<string>.Value));

            Expression body;
            if (matchMode is StringMatchMode.Exact && comparisonType is null)
            {
                body = Expression.Equal(selector.Body, valueExpression);
            }
            else
            {
                var methodName = matchMode switch
                {
                    StringMatchMode.Exact => nameof(string.Equals),
                    StringMatchMode.StartsWith => nameof(string.StartsWith),
                    StringMatchMode.EndsWith => nameof(string.EndsWith),
                    _ => nameof(string.Contains),
                };

                if (comparisonType is null)
                {
                    var method = typeof(string).GetMethod(methodName, [typeof(string)])!;
                    body = Expression.Call(selector.Body, method, valueExpression);
                }
                else
                {
                    var method = typeof(string).GetMethod(methodName, [typeof(string), typeof(StringComparison)])!;
                    body = Expression.Call(selector.Body, method, valueExpression, Expression.Constant(comparisonType.Value));
                }
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
            AddHandlerCore(key, KeyValueOperator.LessThan, value => CreateComparisonExpression(value, selector, ExpressionType.LessThan, parser));
            AddHandlerCore(key, KeyValueOperator.LessThanOrEqual, value => CreateComparisonExpression(value, selector, ExpressionType.LessThanOrEqual, parser));
            AddHandlerCore(key, KeyValueOperator.GreaterThan, value => CreateComparisonExpression(value, selector, ExpressionType.GreaterThan, parser));
            AddHandlerCore(key, KeyValueOperator.GreaterThanOrEqual, value => CreateComparisonExpression(value, selector, ExpressionType.GreaterThanOrEqual, parser));
        }
        else
        {
            // Just register equality
            AddHandlerCore(key, KeyValueOperator.EqualTo, value => CreateComparisonExpression(value, selector, ExpressionType.Equal, parser));
        }
    }

    /// <summary>Registers a custom expression handler for a property key and operator.</summary>
    /// <param name="key">The property key to handle.</param>
    /// <param name="op">The operator to handle.</param>
    /// <param name="handler">Function that creates the filter expression from the query value.</param>
    public void AddHandler(string key, KeyValueOperator op, Func<string, Expression<Func<T, bool>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var lowerKey = key.ToLowerInvariant();
        _handlers[new ExpressionFilterKey(lowerKey, op)] = handler;
        _keys.Add(lowerKey);
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
    /// <exception cref="QueryTooComplexException">The query is too complex to be evaluated.</exception>
    /// <exception cref="InvalidQueryException"><see cref="Strict"/> is <see langword="true"/> and the query has diagnostics.</exception>
    public ExpressionQuery<T> Build(string query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query))
            return new ExpressionQuery<T>(query, predicate: null, diagnostics: []);

        var syntax = QuerySyntax.Parse(query, out var diagnostics);
        diagnostics = QuerySyntax.AddUnknownKeyDiagnostics(syntax, diagnostics, IsKnownKey);
        if (Strict && diagnostics.Count > 0)
            throw new InvalidQueryException(query, diagnostics);

        var boundQuery = BoundQuery.Create(syntax);

        if (boundQuery is [[var single]])
            return new ExpressionQuery<T>(query, CreateExpression(single), diagnostics);

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
        return new ExpressionQuery<T>(query, filter, diagnostics);
    }

    // A key without a handler is searched as text, unless the unhandled property handler takes it
    private bool IsKnownKey(string key) => _unhandledPropertyHandler is not null || _keys.Contains(key.ToLowerInvariant());

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

        // Expression.LessThan is not defined for an enum type: the comparison converts both operands to the
        // underlying type first, which LINQ providers such as Entity Framework Core translate
        if (type.IsEnum)
            return IsComparisonTypeCore(Enum.GetUnderlyingType(type));

        return IsComparisonTypeCore(type);
    }

    private static bool IsComparisonTypeCore(Type type)
    {
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
        ExpressionType comparison,
        ScalarParser<TValue> parser)
    {
        if (!parser(value, out var parsedValue))
            return CreateFalseExpression();

        return CreateComparisonExpressionCore(parsedValue, selector, comparison);
    }

    private static Expression<Func<T, bool>> CreateComparisonExpressionCore<TValue>(
        TValue value,
        Expression<Func<T, TValue>> selector,
        ExpressionType comparison)
    {
        var box = new QueryValueStore<TValue>(value);
        var valueExpression = Expression.PropertyOrField(Expression.Constant(box), nameof(QueryValueStore<TValue>.Value));

        Expression left = selector.Body;
        Expression right = valueExpression;

        // Equality is defined for enums and keeps working with a column stored as text. The ordered comparisons are not,
        // so compare the underlying numeric values: (int)item.Status < (int)value.
        if (comparison is not ExpressionType.Equal && GetEnumUnderlyingComparisonType(typeof(TValue)) is { } underlyingType)
        {
            left = Expression.Convert(left, underlyingType);
            right = Expression.Convert(right, underlyingType);
        }

        var body = Expression.MakeBinary(comparison, left, right);
        return Expression.Lambda<Func<T, bool>>(body, selector.Parameters);
    }

    /// <summary>Returns the underlying type of an enum, or of a nullable enum as a nullable type, or <see langword="null"/> for other types.</summary>
    private static Type? GetEnumUnderlyingComparisonType(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        var enumType = nullableType ?? type;
        if (!enumType.IsEnum)
            return null;

        var underlyingType = Enum.GetUnderlyingType(enumType);
        if (nullableType is null)
            return underlyingType;

        // Listed explicitly rather than with MakeGenericType, which is not trim and AOT friendly
        return Type.GetTypeCode(underlyingType) switch
        {
            TypeCode.SByte => typeof(sbyte?),
            TypeCode.Byte => typeof(byte?),
            TypeCode.Int16 => typeof(short?),
            TypeCode.UInt16 => typeof(ushort?),
            TypeCode.Int32 => typeof(int?),
            TypeCode.UInt32 => typeof(uint?),
            TypeCode.Int64 => typeof(long?),
            TypeCode.UInt64 => typeof(ulong?),
            TypeCode.Char => typeof(char?),
            _ => throw new NotSupportedException($"The underlying type of '{enumType}' is not supported"),
        };
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
                binary.LowerBoundIncluded ? ExpressionType.GreaterThanOrEqual : ExpressionType.GreaterThan);

            var upperExpression = CreateComparisonExpressionCore(
                binary.UpperBound,
                selector,
                binary.UpperBoundIncluded ? ExpressionType.LessThanOrEqual : ExpressionType.LessThan);

            return lowerExpression.AndAlso(upperExpression);
        }

        if (range is UnaryRangeSyntax<TValue> unary)
        {
            var comparison = unary.Operator switch
            {
                KeyValueOperator.LessThan => ExpressionType.LessThan,
                KeyValueOperator.LessThanOrEqual => ExpressionType.LessThanOrEqual,
                KeyValueOperator.GreaterThan => ExpressionType.GreaterThan,
                KeyValueOperator.GreaterThanOrEqual => ExpressionType.GreaterThanOrEqual,
                _ => ExpressionType.Equal,
            };

            return CreateComparisonExpressionCore(unary.Operand, selector, comparison);
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
