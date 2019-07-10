using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF
{
    public sealed class AsyncDelegateCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        private readonly Func<object, bool> _canExecute;
        private readonly Dispatcher _dispatcher;
        private bool _isExecuting;

        public event EventHandler CanExecuteChanged;

        public AsyncDelegateCommand(Func<Task> execute)
            : this(WrapAction(execute))
        {
        }

        public AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute)
            : this(WrapAction(execute), WrapAction(canExecute))
        {
        }

        public AsyncDelegateCommand(Func<object, Task> execute)
            : this(execute, canExecute: null)
        {
        }

        public AsyncDelegateCommand(Func<object, Task> execute, Func<object, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        public async void Execute(object parameter)
        {
            if (_isExecuting)
                return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();
                await _execute?.Invoke(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }

        }

        public void RaiseCanExecuteChanged()
        {
            if (_dispatcher != null)
            {
                _dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
            }
            else
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private static Func<object, Task> WrapAction(Func<Task> action)
        {
            if (action == null)
                return null;

            return _ => action();
        }

        private static Func<object, bool> WrapAction(Func<bool> action)
        {
            if (action == null)
                return null;

            return _ => action();
        }
    }
}
