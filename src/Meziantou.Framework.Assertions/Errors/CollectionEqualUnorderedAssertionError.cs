namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionEqualUnorderedAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, CollectionSnapshot<TActual> actualValue, int? missingExpectedIndex, int? unexpectedActualIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int? MissingExpectedIndex { get; } = missingExpectedIndex;
    public int? UnexpectedActualIndex { get; } = unexpectedActualIndex;
}
