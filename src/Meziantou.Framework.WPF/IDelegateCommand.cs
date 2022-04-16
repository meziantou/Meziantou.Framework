using System.Windows.Input;

namespace Meziantou.Framework.WPF;

public interface IDelegateCommand : ICommand
{
    [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This method raise an existing event")]
    void RaiseCanExecuteChanged();
}
