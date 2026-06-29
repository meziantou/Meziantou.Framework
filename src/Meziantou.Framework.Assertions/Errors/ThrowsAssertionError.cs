namespace Meziantou.Framework.Assertions;

internal readonly ref struct ThrowsAssertionError(Type expectedExceptionType, Exception? actualException, bool allowDerivedTypes, string? actionExpression)
{
    public Type ExpectedExceptionType { get; } = expectedExceptionType;
    public Exception? ActualException { get; } = actualException;
    public bool AllowDerivedTypes { get; } = allowDerivedTypes;
    public string? ActionExpression { get; } = actionExpression;
}
