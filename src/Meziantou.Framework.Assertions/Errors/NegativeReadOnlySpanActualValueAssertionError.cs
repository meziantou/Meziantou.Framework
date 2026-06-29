namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeReadOnlySpanActualValueAssertionError<TActual>(string assertionName, string notExpectedText, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public ReadOnlyMemory<TActual> ActualValue { get; } = actualValue.ToArray();
    public string? ActualExpression { get; } = actualExpression;
    public string? Message { get; } = message;
}
