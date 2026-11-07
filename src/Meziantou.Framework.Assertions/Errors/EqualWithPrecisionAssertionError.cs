namespace Meziantou.Framework.Assertions;

internal readonly struct EqualWithPrecisionAssertionError<TValue, TRounded>(bool isNegative, TValue expectedValue, TValue actualValue, TRounded roundedExpectedValue, TRounded roundedActualValue, int precision, MidpointRounding? rounding, string? message, string? actualExpression, string? expectedExpression)
{
    public bool IsNegative { get; } = isNegative;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public TValue ExpectedValue { get; } = expectedValue;
    public TValue ActualValue { get; } = actualValue;
    public TRounded RoundedExpectedValue { get; } = roundedExpectedValue;
    public TRounded RoundedActualValue { get; } = roundedActualValue;
    public int Precision { get; } = precision;
    public MidpointRounding? Rounding { get; } = rounding;
}
