namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanSingleAssertionError<T>(ReadOnlySpan<T> actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
}
