using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF;

/// <summary>
/// Extension methods for <see cref="Dispatcher"/> to enable async/await patterns.
/// </summary>
/// <example>
/// <code>
/// await dispatcher.SwitchToDispatcherThread();
/// // Code here runs on the UI thread
/// </code>
/// </example>
public static class DispatcherExtensions
{
    /// <summary>Returns an awaitable that switches execution to the dispatcher thread.</summary>
    /// <param name="dispatcher">The dispatcher to switch to.</param>
    /// <returns>An awaitable that switches to the dispatcher thread.</returns>
    // https://medium.com/@kevingosse/switching-back-to-the-ui-thread-in-wpf-uwp-in-modern-c-5dc1cc8efa5e
    public static SwitchToUiAwaitable SwitchToDispatcherThread(this Dispatcher dispatcher)
    {
        return new SwitchToUiAwaitable(dispatcher);
    }

    /// <summary>An awaitable that switches execution to the UI thread.</summary>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct SwitchToUiAwaitable : INotifyCompletion
    {
        private readonly Dispatcher _dispatcher;

        internal SwitchToUiAwaitable(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>Gets the awaiter for this awaitable.</summary>
        /// <returns>This instance.</returns>
        public SwitchToUiAwaitable GetAwaiter()
        {
            return this;
        }

        /// <summary>Ends the await operation.</summary>
        public void GetResult()
        {
        }

        /// <summary>Gets a value indicating whether the awaiter has completed.</summary>
        public bool IsCompleted => _dispatcher.CheckAccess();

        /// <summary>Schedules the continuation action to run on the dispatcher thread.</summary>
        /// <param name="continuation">The continuation action.</param>
        public void OnCompleted(Action continuation)
        {
            _ = _dispatcher.BeginInvoke(continuation);
        }
    }
}
