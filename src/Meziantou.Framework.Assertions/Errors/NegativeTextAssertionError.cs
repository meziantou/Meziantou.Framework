namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeTextAssertionError(string assertionName, string notExpectedText, string actualText, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public string ActualText { get; } = actualText;
    public string? Message { get; } = message;
}
