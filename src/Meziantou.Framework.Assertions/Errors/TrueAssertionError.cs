namespace Meziantou.Framework.Assertions;

internal readonly ref struct TrueAssertionError(string? message, string? expression)
{
    public string? Message { get; } = message;
    public string? Expression { get; } = expression;
    }
