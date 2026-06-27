namespace Meziantou.Framework.Assertions;

internal readonly ref struct FailAssertionError(string? message)
{
    public string? Message { get; } = message;
}
