namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

/// <summary>Represents a bound key-value query.</summary>
public sealed class BoundKeyValueQuery : BoundQuery
{
    internal BoundKeyValueQuery(bool isNegated, string key, string value, KeyValueOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        IsNegated = isNegated;
        Key = key;
        Value = value;
        Operator = @operator;
    }

    /// <summary>Gets a value indicating whether the query is negated.</summary>
    public bool IsNegated { get; }

    /// <summary>Gets the property key.</summary>
    public string Key { get; }

    /// <summary>Gets the value to compare against.</summary>
    public string Value { get; }

    /// <summary>Gets the comparison operator.</summary>
    public KeyValueOperator Operator { get; }
}
