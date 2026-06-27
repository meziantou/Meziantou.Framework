namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeReadOnlySpanValueAssertionError<TExpected, TActual>(string assertionName, string notExpectedLabel, ReadOnlySpan<TExpected> expectedValue, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlyMemory<TExpected> ExpectedValue { get; } = expectedValue.ToArray();
    public ReadOnlyMemory<TActual> ActualValue { get; } = actualValue.ToArray();
}
