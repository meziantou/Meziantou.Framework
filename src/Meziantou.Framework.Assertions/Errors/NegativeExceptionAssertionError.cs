namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeExceptionAssertionError(string assertionName, string notExpectedText, string expression, Type exceptionType, string? exceptionMessage)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public string Expression { get; } = expression;
    public Type ExceptionType { get; } = exceptionType;
    public string? ExceptionMessage { get; } = exceptionMessage;
}
