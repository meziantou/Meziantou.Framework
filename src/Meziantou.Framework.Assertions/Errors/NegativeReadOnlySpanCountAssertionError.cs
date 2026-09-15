namespace Meziantou.Framework.Assertions;

internal readonly ref struct NegativeReadOnlySpanCountAssertionError<TActual>(string assertionName, int notExpectedCount, int actualCount, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public int NotExpectedCount { get; } = notExpectedCount;
    public int ActualCount { get; } = actualCount;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
}
