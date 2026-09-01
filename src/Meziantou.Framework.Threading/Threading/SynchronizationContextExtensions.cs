using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Threading;

public static class SynchronizationContextExtensions
{
    /// <summary>
    /// Gets an awaiter that will post the continuation to the specified synchronization context.
    /// </summary>
    /// <param name="synchronizationContext">The synchronization context to post the continuation to.</param>
    /// <returns>A <see cref="SynchronizationContextAwaiter"/> instance.</returns>
    /// <remarks>
    /// The continuation is posted to the synchronization context. When <see cref="SynchronizationContext.Post"/>
    /// throws — posting to a dispatcher that has already shut down, for instance — the exception surfaces on the
    /// thread that completed the awaited operation and the awaiting method never resumes.
    /// </remarks>
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
            // The continuation travels as the callback state so the lambda stays closure-free and can be cached
            // by the compiler, instead of allocating a display class on every await.
            synchronizationContext.Post(static state => ((Action)state!)(), continuation);
        }
    }
}
