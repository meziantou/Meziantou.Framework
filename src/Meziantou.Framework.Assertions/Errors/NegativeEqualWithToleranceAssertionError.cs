namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeEqualWithToleranceAssertionError<T>(T expectedValue, T actualValue, T tolerance, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public T ActualValue { get; } = actualValue;
    public T Tolerance { get; } = tolerance;
}
