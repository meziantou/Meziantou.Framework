namespace Meziantou.Framework.SimpleQueryLanguage.Binding;

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

    public bool IsNegated { get; }

    public string Key { get; }

    public string Value { get; }

    public KeyValueOperator Operator { get; }
}
