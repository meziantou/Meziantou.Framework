namespace Meziantou.Framework.Assertions;

internal readonly struct RegexMatchesAssertionError(string expectedPattern, string actualValue, string? actualExpression, string? expectedExpression)
{
    public string ExpectedPattern { get; } = expectedPattern;
    public string ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
