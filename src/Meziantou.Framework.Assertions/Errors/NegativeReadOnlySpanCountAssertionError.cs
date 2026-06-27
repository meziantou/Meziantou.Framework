namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeReadOnlySpanCountAssertionError<TActual>(string assertionName, int notExpectedCount, int actualCount, ReadOnlySpan<TActual> actualValue, string? actualExpression)
{
    public string AssertionName { get; } = assertionName;
    public int NotExpectedCount { get; } = notExpectedCount;
    public int ActualCount { get; } = actualCount;
    public ReadOnlyMemory<TActual> ActualValue { get; } = actualValue.ToArray();
    public string? ActualExpression { get; } = actualExpression;
}
