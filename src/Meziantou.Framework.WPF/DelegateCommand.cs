namespace Meziantou.Framework.WPF;

/// <summary>
/// Factory class for creating command instances that can be used with WPF data binding.
/// </summary>
/// <example>
/// <code>
/// public class MyViewModel
/// {
///     public IDelegateCommand SaveCommand { get; }
///     
///     public MyViewModel()
///     {
///         SaveCommand = DelegateCommand.Create(Save, CanSave);
///     }
///     
///     private void Save()
///     {
///         // Save logic
///     }
///     
///     private bool CanSave()
///     {
///         return true;
///     }
/// }
/// </code>
/// </example>
public static class DelegateCommand
{
    /// <summary>Creates a command that executes the specified action.</summary>
    /// <param name="execute">The action to execute when the command is invoked.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Action? execute)
    {
        return new SyncDelegateCommand(WrapAction(execute), CanExecuteTrue);
    }

    /// <summary>Creates a command that executes the specified action with a parameter.</summary>
    /// <param name="execute">The action to execute when the command is invoked.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Action<object?>? execute)
    {
        return new SyncDelegateCommand(execute ?? DefaultExecute, CanExecuteTrue);
    }

    /// <summary>Creates a command that executes the specified action and checks if it can execute.</summary>
    /// <param name="execute">The action to execute when the command is invoked.</param>
    /// <param name="canExecute">The function that determines if the command can execute.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Action? execute, Func<bool>? canExecute)
    {
        return new SyncDelegateCommand(WrapAction(execute), WrapAction(canExecute));
    }

    /// <summary>Creates a command that executes the specified action with a parameter and checks if it can execute.</summary>
    /// <param name="execute">The action to execute when the command is invoked.</param>
    /// <param name="canExecute">The function that determines if the command can execute.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Action<object?>? execute, Func<object?, bool>? canExecute)
    {
        return new SyncDelegateCommand(execute ?? DefaultExecute, canExecute ?? CanExecuteTrue);
    }

    /// <summary>Creates an asynchronous command that executes the specified task.</summary>
    /// <param name="execute">The task to execute when the command is invoked.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Func<Task>? execute)
    {
        return new AsyncDelegateCommand(WrapAction(execute), CanExecuteTrue);
    }

    /// <summary>Creates an asynchronous command that executes the specified task with a parameter.</summary>
    /// <param name="execute">The task to execute when the command is invoked.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Func<object?, Task>? execute)
    {
        return new AsyncDelegateCommand(execute ?? DefaultExecuteAsync, CanExecuteTrue);
    }

    /// <summary>Creates an asynchronous command that executes the specified task and checks if it can execute.</summary>
    /// <param name="execute">The task to execute when the command is invoked.</param>
    /// <param name="canExecute">The function that determines if the command can execute.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Func<Task>? execute, Func<bool>? canExecute)
    {
        return new AsyncDelegateCommand(WrapAction(execute), WrapAction(canExecute));
    }

    /// <summary>Creates an asynchronous command that executes the specified task with a parameter and checks if it can execute.</summary>
    /// <param name="execute">The task to execute when the command is invoked.</param>
    /// <param name="canExecute">The function that determines if the command can execute.</param>
    /// <returns>A new <see cref="IDelegateCommand"/> instance.</returns>
    public static IDelegateCommand Create(Func<object?, Task>? execute, Func<object?, bool>? canExecute)
    {
        return new AsyncDelegateCommand(execute ?? DefaultExecuteAsync, canExecute ?? CanExecuteTrue);
    }

    private static void DefaultExecute(object? _)
    {
    }

    private static Task DefaultExecuteAsync(object? _) => Task.CompletedTask;

    private static bool CanExecuteTrue(object? _) => true;

    private static Func<object?, Task> WrapAction(Func<Task>? action)
    {
        if (action is null)
            return DefaultExecuteAsync;

        return _ => action();
    }

    private static Action<object?> WrapAction(Action? action)
    {
        if (action is null)
            return DefaultExecute;

        return _ => action();
    }

    private static Func<object?, bool> WrapAction(Func<bool>? action)
    {
        if (action is null)
            return CanExecuteTrue;

        return _ => action();
    }
}
