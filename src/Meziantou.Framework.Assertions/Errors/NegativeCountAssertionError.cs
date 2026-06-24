namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeCountAssertionError<TActual>(string assertionName, int notExpectedCount, int actualCount, TActual actualValue, string? actualExpression)
{
    public string AssertionName { get; } = assertionName;
    public int NotExpectedCount { get; } = notExpectedCount;
    public int ActualCount { get; } = actualCount;
    public TActual ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
}
