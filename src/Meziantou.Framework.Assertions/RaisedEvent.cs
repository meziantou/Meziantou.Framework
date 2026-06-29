namespace Meziantou.Framework.Assertions;

public sealed class RaisedEvent<TEventArgs>(object? sender, TEventArgs arguments)
    where TEventArgs : EventArgs
{
    public object? Sender { get; } = sender;
    public TEventArgs Arguments { get; } = arguments;
}
