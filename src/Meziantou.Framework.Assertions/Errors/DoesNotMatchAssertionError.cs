namespace Meziantou.Framework.Assertions;

internal readonly struct DoesNotMatchAssertionError(string notExpectedLabel, string expectedValue, string actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public string ExpectedValue { get; } = expectedValue;
    public string ActualValue { get; } = actualValue;
}
