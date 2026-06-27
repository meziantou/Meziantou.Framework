namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanLengthAssertionError<TExpected, TActual>(ReadOnlySpan<TExpected> expectedValue, ReadOnlySpan<TActual> actualValue, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<TExpected> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
}
