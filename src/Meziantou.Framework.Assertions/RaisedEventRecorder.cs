namespace Meziantou.Framework.Assertions;

// Records the events raised while an event assertion runs. The handler can be invoked concurrently from several
// threads, and even after it is detached (a raise that captured the delegate before the detach), so every access to
// the list is synchronized and the assertion evaluates a snapshot.
internal sealed class RaisedEventRecorder<TEventArgs>
    where TEventArgs : EventArgs
{
    private readonly Lock _lock = new();
    private readonly List<(object? Sender, TEventArgs? Arguments)> _events = [];

    // The arguments are nullable because nothing prevents an event from being raised with null arguments
    public void Record(object? sender, TEventArgs? arguments)
    {
        lock (_lock)
        {
            _events.Add((sender, arguments));
        }
    }

    public (object? Sender, TEventArgs? Arguments)[] GetEvents()
    {
        lock (_lock)
        {
            return [.. _events];
        }
    }
}
