using System;
using System.Windows.Input;

namespace Meziantou.Framework.WPF
{
    public sealed class DelegateCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        public DelegateCommand(Action execute)
            : this(WrapAction(execute))
        {
        }

        public DelegateCommand(Action execute, Func<bool> canExecute)
            : this(WrapAction(execute), WrapAction(canExecute))
        {
        }

        public DelegateCommand(Action<object> execute)
            : this(execute, null)
        {
        }

        public DelegateCommand(Action<object> execute, Func<object, bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            _execute?.Invoke(parameter);
        }

        private static Action<object> WrapAction(Action action)
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
