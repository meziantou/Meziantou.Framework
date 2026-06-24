namespace Meziantou.Framework.Assertions;

internal readonly ref struct FalseAssertionError(bool? actual, string? message, string? expression)
{
    public bool? Actual { get; } = actual;
    public string? Message { get; } = message;
    public string? Expression { get; } = expression;
}
