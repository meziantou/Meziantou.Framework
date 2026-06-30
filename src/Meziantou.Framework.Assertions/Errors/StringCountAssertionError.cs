namespace Meziantou.Framework.Assertions;

internal readonly ref struct StringCountAssertionError(string assertionName, string expectedCount, int actualCount, ReadOnlySpan<char> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string ExpectedCount { get; } = expectedCount;
    public int ActualCount { get; } = actualCount;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<char> ActualValue { get; } = actualValue;
}
