namespace Meziantou.Framework.Assertions;

internal readonly ref struct ThrowsParameterNameAssertionError(ArgumentException actualException, string? expectedParamName, string? actionExpression, string? message = null)
{
    public string? Message { get; } = message;
    public ArgumentException ActualException { get; } = actualException;
    public string? ExpectedParamName { get; } = expectedParamName;
    public string? ActionExpression { get; } = actionExpression;
}
