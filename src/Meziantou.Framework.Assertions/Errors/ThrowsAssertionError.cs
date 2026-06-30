namespace Meziantou.Framework.Assertions;

internal readonly ref struct ThrowsAssertionError(Type expectedExceptionType, Exception? actualException, bool allowDerivedTypes, string? actionExpression, string? message = null)
{
    public string? Message { get; } = message;
    public Type ExpectedExceptionType { get; } = expectedExceptionType;
    public Exception? ActualException { get; } = actualException;
    public bool AllowDerivedTypes { get; } = allowDerivedTypes;
    public string? ActionExpression { get; } = actionExpression;
}
