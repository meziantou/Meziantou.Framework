using System.Windows;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF;

/// <summary>Shared dispatcher handling for the <see cref="IDelegateCommand"/> implementations.</summary>
internal static class DelegateCommandDispatcher
{
    /// <summary>Gets the dispatcher of the calling thread, or <see langword="null"/> when that thread has none.</summary>
    /// <remarks>
    /// <see cref="Dispatcher.CurrentDispatcher"/> must not be used here: it creates a dispatcher for the calling thread
    /// when there is none. A command created on a thread pool thread would then be bound to a dispatcher whose thread
    /// never runs a message pump, and every notification sent to it would be queued and never dispatched.
    /// </remarks>
    public static Dispatcher? GetCurrentThreadDispatcher()
    {
        return Dispatcher.FromThread(Thread.CurrentThread);
    }

    /// <summary>Raises <paramref name="handler"/> on the UI thread.</summary>
    /// <param name="dispatcher">The dispatcher captured when the command was created, or <see langword="null"/> when it was not created on a dispatcher thread.</param>
    /// <param name="sender">The command raising the event.</param>
    /// <param name="handler">The handlers to invoke, or <see langword="null"/> when nobody subscribed.</param>
    /// <remarks>
    /// The event is raised synchronously when the caller is already on the target thread, so the common case keeps the
    /// ordering callers expect. Otherwise it is posted with <see cref="Dispatcher.BeginInvoke(Delegate, object?[])"/>
    /// rather than <see cref="Dispatcher.Invoke(Action)"/>: a command bound to a dispatcher that is not pumping
    /// messages then loses the notification instead of blocking the caller forever.
    /// </remarks>
    public static void RaiseCanExecuteChanged(Dispatcher? dispatcher, IDelegateCommand sender, EventHandler? handler)
    {
        if (handler is null)
            return;

        dispatcher ??= Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            handler(sender, EventArgs.Empty);
        }
        else
        {
            _ = dispatcher.BeginInvoke(() => handler(sender, EventArgs.Empty));
        }
    }
}
