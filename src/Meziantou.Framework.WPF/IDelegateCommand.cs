using System.Windows.Input;

namespace Meziantou.Framework.WPF;

public interface IDelegateCommand : ICommand
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "This mehtod raise an existing event")]
    void RaiseCanExecuteChanged();
}
