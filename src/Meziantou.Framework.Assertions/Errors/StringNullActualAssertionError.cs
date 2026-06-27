namespace Meziantou.Framework.Assertions;

internal readonly struct StringNullActualAssertionError(string assertionName, string expectedValueLabel, string expectedValue, StringComparison comparison, string? actualExpression, string? expectedExpression)
{
    public string AssertionName { get; } = assertionName;
    public string ExpectedValueLabel { get; } = expectedValueLabel;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public string ExpectedValue { get; } = expectedValue;
    public StringComparison Comparison { get; } = comparison;
}
