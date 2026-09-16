using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides extension methods for <see cref="SynchronizationContext"/>.</summary>
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

    /// <summary>Awaits a <see cref="SynchronizationContext"/>, resuming the awaiting method on that context.</summary>
    /// <param name="synchronizationContext">The synchronization context to resume on.</param>
    public readonly struct SynchronizationContextAwaiter(SynchronizationContext synchronizationContext) : ICriticalNotifyCompletion
    {
        /// <summary>Gets a value indicating whether the current synchronization context is the awaited one, in which case the awaiting method continues synchronously.</summary>
        /// <remarks>
        /// The synchronization contexts are compared by instance. A different instance bound to the same thread, such as
        /// the ones WPF installs while running a dispatcher operation, is not recognized: the continuation is then posted,
        /// which is still correct but does not complete synchronously.
        /// </remarks>
        public bool IsCompleted => SynchronizationContext.Current == synchronizationContext;

        /// <summary>Ends the await. This method does nothing.</summary>
        public void GetResult()
        {
        }

        /// <summary>Posts <paramref name="continuation"/> to the synchronization context.</summary>
        /// <param name="continuation">The action to invoke once the awaiting method resumes.</param>
        public void OnCompleted(Action continuation)
        {
            Post(continuation);
        }

        /// <summary>Posts <paramref name="continuation"/> to the synchronization context.</summary>
        /// <param name="continuation">The action to invoke once the awaiting method resumes.</param>
        /// <remarks>The async method builders call this method and flow the <see cref="ExecutionContext"/> themselves.</remarks>
        public void UnsafeOnCompleted(Action continuation)
        {
            Post(continuation);
        }

        private void Post(Action continuation)
        {
            // The continuation travels as the callback state so the lambda stays closure-free and can be cached
            // by the compiler, instead of allocating a display class on every await.
            synchronizationContext.Post(static state => ((Action)state!)(), continuation);
        }
    }
}
