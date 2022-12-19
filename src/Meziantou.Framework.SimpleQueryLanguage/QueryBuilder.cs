using System.Diagnostics;
using Meziantou.Framework.SimpleQueryLanguage.Binding;
using Meziantou.Framework.SimpleQueryLanguage.Ranges;
using Meziantou.Framework.SimpleQueryLanguage.Syntax;

namespace Meziantou.Framework.SimpleQueryLanguage;

public sealed class QueryBuilder<T>
{
    private static readonly Predicate<T> AlwaysFalsePredicate = _ => false;
    private static readonly Predicate<T> AlwaysTruePredicate = _ => true;

    private readonly Dictionary<FilterKeyValue, Func<T, KeyValueOperator, string, bool>> _filters = new();
    private Func<T, string, bool>? _freeTextFilter;
    private UnhandledPropertyDelegate<T>? _unhandledPropertyFilter;

    private void AddHandler(string key, string? value, Func<T, KeyValueOperator, string, bool> handler)
    {
        _filters.Add(new FilterKeyValue(key.ToLowerInvariant(), value?.ToLowerInvariant()), handler);
    }

    public void AddHandler(string key, string? value, Func<T, KeyValueOperator, bool> predicate)
    {
        AddHandler(key, value, (T obj, KeyValueOperator op, string value) => predicate(obj, op));
    }

    public void AddHandler(string key, Func<T, KeyValueOperator, string, bool> predicate)
    {
        AddHandler(key, value: null, predicate);
    }

    public void AddHandler<TValue>(string key, Func<T, KeyValueOperator, TValue, bool> predicate)
    {
        AddHandler(key, predicate, tryParseValue: null);
    }

    public void AddHandler<TValue>(string key, Func<T, KeyValueOperator, TValue, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        bool CreatePredicate(T obj, KeyValueOperator op, string value)
        {
            var tryParse = tryParseValue ?? ValueConverter.TryParseValue<TValue>;
            if (tryParse(value, out var parsedValue))
                return predicate(obj, op, parsedValue);

            return false;
        }

        AddHandler(key, value: null, CreatePredicate);
    }

    public void AddHandler(string key, string? value, Func<T, bool> predicate)
    {
        AddHandler(key, value, ApplyEqualOperator((T obj, string value) => predicate(obj)));
    }

    public void AddHandler(string key, Func<T, string, bool> predicate)
    {
        AddHandler(key, value: null, ApplyEqualOperator((T obj, string value) => predicate(obj, value)));
    }

    public void AddHandler<TValue>(string key, Func<T, TValue, bool> predicate)
    {
        AddHandler(key, predicate, tryParseValue: null);
    }

    public void AddHandler<TValue>(string key, Func<T, TValue, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        bool CreatePredicate(T obj, KeyValueOperator op, string value)
        {
            var tryParse = tryParseValue ?? ValueConverter.TryParseValue<TValue>;
            if (tryParse(value, out var parsedValue))
                return predicate(obj, parsedValue);

            return false;
        }

        AddHandler(key, value: null, CreatePredicate);
    }

    public void AddRangeHandler<TValue>(string key, Func<T, RangeSyntax<TValue>, bool> predicate, ScalarParser<TValue>? tryParseValue)
    {
        bool CreatePredicate(T obj, KeyValueOperator op, string value)
        {
            var tryParse = tryParseValue ?? ValueConverter.TryParseValue<TValue>;
            return ConvertRangePredicate(obj, op, value, predicate, tryParse);
        }

        AddHandler(key, value: null, CreatePredicate);
    }

    public void AddRangeHandler<TValue>(string key, Func<T, RangeSyntax<TValue>, bool> predicate)
    {
        AddRangeHandler(key, predicate, tryParseValue: null);
    }

    // Ranges
    private static bool ConvertRangePredicate<TValue>(T obj, KeyValueOperator op, string value, Func<T, RangeSyntax<TValue>, bool> predicate, ScalarParser<TValue> tryParseValue)
    {
        // field:1..10
        // field=1..10
        if (op == KeyValueOperator.EqualTo)
        {
            var range = RangeSyntax.TryParse(value, tryParseValue);
            if (range != null)
                return predicate(obj, range);
        }
        else if (op == KeyValueOperator.NotEqualTo)
        {
            var range = RangeSyntax.TryParse(value, tryParseValue);
            if (range != null)
                return !predicate(obj, range);
        }
        else
        {
            // field>=1
            if (ValueConverter.TryParseValue<TValue>(value, out var parsedValue))
            {
                var range = new UnaryRangeSyntax<TValue>(op, parsedValue);
                return predicate(obj, range);
            }
        }

        return false;
    }

    private static Func<T, KeyValueOperator, string, bool> ApplyEqualOperator(Func<T, string, bool> predicate)
    {
        return new Func<T, KeyValueOperator, string, bool>((obj, op, value) =>
        {
            if (op is KeyValueOperator.EqualTo)
                return predicate(obj, value);

            if (op is KeyValueOperator.NotEqualTo)
                return !predicate(obj, value);

            // Not supported
            return false;
        });
    }

    public void SetTextFilterHandler(Func<T, string, bool> predicate)
    {
        _freeTextFilter = predicate;
    }

    public void SetUnhandledPropertyHandler(UnhandledPropertyDelegate<T>? predicate)
    {
        _unhandledPropertyFilter = predicate;
    }

    public Query<T> Build(string query)
    {
        var predicate = CreatePredicate(query);
        return new Query<T>(query, predicate);
    }

    private Predicate<T> CreatePredicate(string query)
    {
        if (query.Length == 0)
            return AlwaysTruePredicate;

        var syntax = QuerySyntax.Parse(query);
        var boundQuery = BoundQuery.Create(syntax);

        Predicate<T>? predicate = null;

        foreach (var disjunction in boundQuery)
        {
            Predicate<T>? disjunctionPredicate = null;

            foreach (var conjunction in disjunction)
            {
                var next = CreatePredicate(conjunction);
                var current = disjunctionPredicate;
                disjunctionPredicate = current is null
                    ? next
                    : new Predicate<T>(v => current(v) && next(v));
            }

            if (disjunctionPredicate is not null)
            {
                var next = disjunctionPredicate;
                var current = predicate;
                predicate = current is null
                    ? next
                    : new Predicate<T>(v => current(v) || next(v));
            }
        }

        return predicate ?? AlwaysTruePredicate;
    }

    private Predicate<T> CreatePredicate(BoundQuery node)
    {
        return node switch
        {
            BoundTextQuery textQuery => CreatePredicate(textQuery),
            BoundKeyValueQuery keyValueQuery => CreatePredicate(keyValueQuery),
            _ => throw new ArgumentOutOfRangeException(nameof(node), $"Unexpected node: {node.GetType()}")
        };
    }

    private Predicate<T> CreatePredicate(BoundTextQuery node)
    {
        if (_freeTextFilter == null)
            return AlwaysFalsePredicate;

        return node.IsNegated
                ? wi => !_freeTextFilter(wi, node.Text)
                : wi => _freeTextFilter(wi, node.Text);
    }

    private Predicate<T> CreatePredicate(BoundKeyValueQuery node)
    {
        var key = node.Key.ToLowerInvariant();
        var value = node.Value.ToLowerInvariant();
        var op = node.Operator;

        var handlers = _filters;

        if (handlers.TryGetValue(new FilterKeyValue(key, value), out var predicateHandler) ||
            handlers.TryGetValue(new FilterKeyValue(key, value: null), out predicateHandler))
        {
            return node.IsNegated
                    ? v => !predicateHandler(v, op, node.Value)
                    : v => predicateHandler(v, op, node.Value);
        }

        if (_unhandledPropertyFilter != null)
        {
            return v => _unhandledPropertyFilter(v, node.Key, op, node.Value);
        }

        return CreatePredicate(new BoundTextQuery(node.IsNegated, $"{node.Key}:{node.Value}"));
    }

    private readonly record struct FilterKeyValue
    {
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
