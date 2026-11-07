namespace Meziantou.Framework.Assertions;

internal readonly struct DoesNotContainAssertionError<TExpected, TActual>(string notExpectedLabel, TExpected expectedValue, TActual actualValue, string? actualExpression, string? expectedExpression, string? message, int? foundIndex = null, string foundIndexLabel = "Index of found item")
{
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public TExpected ExpectedValue { get; } = expectedValue;
    public TActual ActualValue { get; } = actualValue;

    /// <summary>Gets the index in <see cref="ActualValue"/> where the forbidden item, key, subsequence or substring was found, when it is known.</summary>
    public int? FoundIndex { get; } = foundIndex;
    public string FoundIndexLabel { get; } = foundIndexLabel;
}
