using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF;

/// <summary>
/// Extension methods for <see cref="Dispatcher"/> to enable async/await patterns.
/// </summary>
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
    /// <remarks>
    /// Instances must be obtained from <see cref="SwitchToDispatcherThread(Dispatcher)"/>. A default-initialized value
    /// has no dispatcher to switch to and throws <see cref="InvalidOperationException"/> when awaited.
    /// </remarks>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct SwitchToUiAwaitable : INotifyCompletion
    {
        // Nullable even though the constructor never receives null: the type is a public struct, so
        // default(SwitchToUiAwaitable) is reachable from user code and leaves this field null.
        private readonly Dispatcher? _dispatcher;

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
        /// <exception cref="InvalidOperationException">The instance was default-initialized instead of being obtained from <see cref="SwitchToDispatcherThread(Dispatcher)"/>.</exception>
        public bool IsCompleted => GetDispatcher().CheckAccess();

        /// <summary>Schedules the continuation action to run on the dispatcher thread.</summary>
        /// <param name="continuation">The continuation action.</param>
        /// <exception cref="InvalidOperationException">The instance was default-initialized instead of being obtained from <see cref="SwitchToDispatcherThread(Dispatcher)"/>, or the dispatcher has already shut down.</exception>
        public void OnCompleted(Action continuation)
        {
            // The operation is aborted when the dispatcher shuts down before it gets a chance to run. Resuming the
            // continuation in that case lets the caller observe the shutdown; leaving it unscheduled would make the
            // awaiting task hang forever with nothing to diagnose.
            var resumption = new Resumption(continuation);
            var operation = GetDispatcher().BeginInvoke(resumption.Resume);
            operation.Aborted += resumption.OnAborted;

            // The operation may have been aborted before the handler above was attached.
            if (operation.Status is DispatcherOperationStatus.Aborted)
            {
                resumption.Resume();
            }
        }

        private Dispatcher GetDispatcher()
        {
            return _dispatcher ?? throw new InvalidOperationException($"This '{nameof(SwitchToUiAwaitable)}' has no dispatcher. Use '{nameof(SwitchToDispatcherThread)}' to create one.");
        }

        /// <summary>Ensures the continuation runs at most once, whether the operation completes or is aborted.</summary>
        private sealed class Resumption
        {
            private Action? _continuation;

            public Resumption(Action continuation)
            {
                _continuation = continuation;
            }

            public void Resume()
            {
                Interlocked.Exchange(ref _continuation, value: null)?.Invoke();
            }

            public void OnAborted(object? sender, EventArgs e)
            {
                Resume();
            }
        }
    }
}
