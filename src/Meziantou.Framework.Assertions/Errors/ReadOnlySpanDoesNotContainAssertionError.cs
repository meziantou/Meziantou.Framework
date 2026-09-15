namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanDoesNotContainAssertionError<T>(string notExpectedLabel, ReadOnlySpan<T> expectedValue, ReadOnlySpan<T> actualValue, int foundIndex, string foundIndexLabel, string? actualExpression, string? expectedExpression, string? message)
{
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
    public int FoundIndex { get; } = foundIndex;
    public string FoundIndexLabel { get; } = foundIndexLabel;
}
