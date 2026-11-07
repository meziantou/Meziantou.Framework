using System.Windows.Threading;

namespace Meziantou.Framework.WPF;

internal sealed class SyncDelegateCommand : IDelegateCommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool> _canExecute;
    private readonly Dispatcher? _dispatcher;

    public event EventHandler? CanExecuteChanged;

    public SyncDelegateCommand(Action<object?> execute, Func<object?, bool> canExecute)
    {
        _execute = execute;
        _canExecute = canExecute;
        _dispatcher = DelegateCommandDispatcher.GetCurrentThreadDispatcher();
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute.Invoke(parameter);
    }

    public void Execute(object? parameter)
    {
        _execute.Invoke(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        DelegateCommandDispatcher.RaiseCanExecuteChanged(_dispatcher, this, CanExecuteChanged);
    }
}
