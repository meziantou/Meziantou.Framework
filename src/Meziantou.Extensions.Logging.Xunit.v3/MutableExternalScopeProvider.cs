using Microsoft.Extensions.Logging;

#pragma warning disable IDE1006 // Naming Styles
namespace Meziantou.Extensions.Logging.Xunit.v3;
#pragma warning restore IDE1006 // Naming Styles

// ILoggerFactory hands a provider its own scope provider through ISupportExternalScope, but only
// once the provider is registered. Loggers therefore hold this indirection instead of a specific
// IExternalScopeProvider, so a logger created before that hand-off still observes the factory's
// scopes afterwards.
internal sealed class MutableExternalScopeProvider : IExternalScopeProvider
{
    private IExternalScopeProvider _current;

    public MutableExternalScopeProvider(IExternalScopeProvider current)
    {
        _current = current;
    }

    public IExternalScopeProvider Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value);
    }

    public void ForEachScope<TState>(Action<object?, TState> callback, TState state) => Current.ForEachScope(callback, state);

    public IDisposable Push(object? state) => Current.Push(state);
}
