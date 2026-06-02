using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Threading;

public static class SynchronizationContextExtensions
{
    /// <summary>
    /// Gets an awaiter that will post the continuation to the specified synchronization context.
    /// </summary>
    /// <param name="synchronizationContext">The synchronization context to post the continuation to.</param>
    /// <returns>A <see cref="SynchronizationContextAwaiter"/> instance.</returns>
    public static SynchronizationContextAwaiter GetAwaiter(this SynchronizationContext synchronizationContext)
    {
        ArgumentNullException.ThrowIfNull(synchronizationContext);
        return new SynchronizationContextAwaiter(synchronizationContext);
    }

    public readonly struct SynchronizationContextAwaiter(SynchronizationContext synchronizationContext) : INotifyCompletion
    {
        public bool IsCompleted => SynchronizationContext.Current == synchronizationContext;

        public void GetResult()
        {
        }

        public void OnCompleted(Action continuation)
        {
            synchronizationContext.Post(_ => continuation(), null);
        }
    }
}
