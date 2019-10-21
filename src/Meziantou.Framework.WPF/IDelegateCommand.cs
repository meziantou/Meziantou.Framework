#nullable disable
using System.Windows.Input;

namespace Meziantou.Framework.WPF
{
    public interface IDelegateCommand : ICommand
    {
        void RaiseCanExecuteChanged();
    }
}
