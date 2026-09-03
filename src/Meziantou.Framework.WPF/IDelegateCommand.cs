using System.Windows.Input;

namespace Meziantou.Framework.WPF;

/// <summary>Extends <see cref="ICommand"/> with a method to raise the CanExecuteChanged event.</summary>
public interface IDelegateCommand : ICommand
{
    /// <summary>Raises the CanExecuteChanged event to notify that the command's execution state has changed.</summary>
    /// <remarks>
    /// The event is raised on the UI thread. When the caller is already on that thread the handlers run synchronously;
    /// otherwise the notification is posted to the dispatcher and this method returns immediately.
    /// </remarks>
    [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This method raise an existing event")]
    void RaiseCanExecuteChanged();
}
