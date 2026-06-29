namespace Meziantou.Framework.Assertions;

internal readonly ref struct StringEmptyAssertionError(ReadOnlySpan<char> actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<char> ActualValue { get; } = actualValue;
}
