namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual>(AsyncCollectionSnapshot<TExpected> expectedValue, AsyncCollectionSnapshot<TActual> actualValue, int? missingExpectedIndex, int? unexpectedActualIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public AsyncCollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public AsyncCollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int? MissingExpectedIndex { get; } = missingExpectedIndex;
    public int? UnexpectedActualIndex { get; } = unexpectedActualIndex;
}
