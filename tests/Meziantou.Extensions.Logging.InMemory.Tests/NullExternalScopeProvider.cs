#nullable enable
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory.Tests;
internal sealed class NullExternalScopeProvider : IExternalScopeProvider
{
    private NullExternalScopeProvider()
    {
    }

    /// <summary>
    /// Returns a cached instance of <see cref="NullExternalScopeProvider"/>.
    /// </summary>
    public static IExternalScopeProvider Instance { get; } = new NullExternalScopeProvider();

    /// <inheritdoc />
    void IExternalScopeProvider.ForEachScope<TState>(Action<object?, TState> callback, TState state)
    {
    }

    /// <inheritdoc />
    IDisposable IExternalScopeProvider.Push(object? state)
    {
        return NullScope.Instance;
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}