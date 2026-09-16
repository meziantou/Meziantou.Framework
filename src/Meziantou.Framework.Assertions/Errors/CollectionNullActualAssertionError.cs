namespace Meziantou.Framework.Assertions;

/// <summary>Reports a null collection passed to an assertion that inspects its items or its count.</summary>
internal readonly struct CollectionNullActualAssertionError(string assertionName, string? actualExpression, string? expectedLabel = null, string? expectedText = null, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedLabel { get; } = expectedLabel;
    public string? ExpectedText { get; } = expectedText;
}
