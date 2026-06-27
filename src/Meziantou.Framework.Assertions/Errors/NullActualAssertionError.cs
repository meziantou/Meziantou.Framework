namespace Meziantou.Framework.Assertions;

internal readonly ref struct NullActualAssertionError<TExpected>(string assertionName, string expectedExpressionLabel, string expectedValueLabel, TExpected expectedValue, string? actualExpression, string? expectedExpression)
{
    public string AssertionName { get; } = assertionName;
    public string ExpectedExpressionLabel { get; } = expectedExpressionLabel;
    public string ExpectedValueLabel { get; } = expectedValueLabel;
    public TExpected ExpectedValue { get; } = expectedValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
