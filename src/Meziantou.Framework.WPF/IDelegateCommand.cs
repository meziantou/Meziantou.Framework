using System.Windows.Input;

namespace Meziantou.Framework.WPF;

/// <summary>Extends <see cref="ICommand"/> with a method to raise the CanExecuteChanged event.</summary>
public interface IDelegateCommand : ICommand
{
    /// <summary>Raises the CanExecuteChanged event to notify that the command's execution state has changed.</summary>
    [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This method raise an existing event")]
    void RaiseCanExecuteChanged();
}
