namespace Meziantou.Framework.Assertions;

internal readonly ref struct StringSingleAssertionError(ReadOnlySpan<char> actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<char> ActualValue { get; } = actualValue;
}
