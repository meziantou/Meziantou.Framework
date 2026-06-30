namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanSingleAssertionError<T>(ReadOnlySpan<T> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
}
