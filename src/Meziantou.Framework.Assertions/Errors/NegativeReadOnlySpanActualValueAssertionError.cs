namespace Meziantou.Framework.Assertions;

internal readonly ref struct NegativeReadOnlySpanActualValueAssertionError<TActual>(string assertionName, string notExpectedText, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? Message { get; } = message;
}
