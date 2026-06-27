namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeExpressionAssertionError(string assertionName, string notExpectedText, string expression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public string Expression { get; } = expression;
    public string? Message { get; } = message;
}
