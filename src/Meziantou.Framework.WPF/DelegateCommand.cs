using System;
using System.Threading.Tasks;

namespace Meziantou.Framework.WPF
{
    public static class DelegateCommand
    {
        public static IDelegateCommand Create(Action execute)
        {
            return new SyncDelegateCommand(WrapAction(execute), null);
        }

        public static IDelegateCommand Create(Action<object> execute)
        {
            return new SyncDelegateCommand(execute, null);
        }

        public static IDelegateCommand Create(Action execute, Func<bool> canExecute)
        {
            return new SyncDelegateCommand(WrapAction(execute), WrapAction(canExecute));
        }

        public static IDelegateCommand Create(Action<object> execute, Func<object, bool> canExecute)
        {
            return new SyncDelegateCommand(execute, canExecute);
        }

        public static IDelegateCommand Create(Func<Task> execute)
        {
            return new AsyncDelegateCommand(WrapAction(execute), null);
        }

        public static IDelegateCommand Create(Func<object, Task> execute)
        {
            return new AsyncDelegateCommand(execute, null);
        }

        public static IDelegateCommand Create(Func<Task> execute, Func<bool> canExecute)
        {
            return new AsyncDelegateCommand(WrapAction(execute), WrapAction(canExecute));
        }

        public static IDelegateCommand Create(Func<object, Task> execute, Func<object, bool> canExecute)
        {
            return new AsyncDelegateCommand(execute, canExecute);
        }

        private static Func<object, Task> WrapAction(Func<Task> action)
        {
            if (action == null)
                return null;

            return _ => action();
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
