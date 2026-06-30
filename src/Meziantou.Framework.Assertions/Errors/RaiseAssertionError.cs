namespace Meziantou.Framework.Assertions;

internal readonly ref struct RaiseAssertionError(Type expectedEventArgsType, Type? actualEventArgsType, bool allowDerivedTypes, string? actionExpression, string? message = null)
{
    public string? Message { get; } = message;
    public Type ExpectedEventArgsType { get; } = expectedEventArgsType;
    public Type? ActualEventArgsType { get; } = actualEventArgsType;
    public bool AllowDerivedTypes { get; } = allowDerivedTypes;
    public string? ActionExpression { get; } = actionExpression;
}
