namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// An <see cref="IUndoRedoAction"/> defined by a pair of delegates, so an action can be recorded
/// without writing a dedicated subclass. The <c>execute</c> and <c>unexecute</c> delegates can each
/// be synchronous (<see cref="Action"/>) or asynchronous (<see cref="Func{T, TResult}"/> returning a
/// <see cref="ValueTask"/>), and the two may be mixed (e.g. an async execute with a sync unexecute).
/// </summary>
/// <example>
/// <code>
/// // Asynchronous execute, synchronous unexecute
/// var action = new UndoRedoDelegateAction(
///     execute: ct => SaveAsync(ct),
///     unexecute: () => Restore());
/// </code>
/// </example>
public sealed class UndoRedoDelegateAction : UndoRedoActionBase
{
    private readonly Func<CancellationToken, ValueTask> _execute;
    private readonly Func<CancellationToken, ValueTask> _unexecute;

    /// <summary>Initializes a new instance with an asynchronous execute delegate and an asynchronous unexecute delegate.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    public UndoRedoDelegateAction(Func<CancellationToken, ValueTask> execute, Func<CancellationToken, ValueTask> unexecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(unexecute);

        _execute = execute;
        _unexecute = unexecute;
    }

    /// <summary>Initializes a new instance with an asynchronous execute delegate and a synchronous unexecute delegate.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    public UndoRedoDelegateAction(Func<CancellationToken, ValueTask> execute, Action unexecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(unexecute);

        _execute = execute;
        _unexecute = Wrap(unexecute);
    }

    /// <summary>Initializes a new instance with a synchronous execute delegate and an asynchronous unexecute delegate.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    public UndoRedoDelegateAction(Action execute, Func<CancellationToken, ValueTask> unexecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(unexecute);

        _execute = Wrap(execute);
        _unexecute = unexecute;
    }

    /// <summary>Initializes a new instance with a synchronous execute delegate and a synchronous unexecute delegate.</summary>
    /// <param name="execute">The delegate invoked to apply the change.</param>
    /// <param name="unexecute">The delegate invoked to revert the change.</param>
    public UndoRedoDelegateAction(Action execute, Action unexecute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(unexecute);

        _execute = Wrap(execute);
        _unexecute = Wrap(unexecute);
    }

    private static Func<CancellationToken, ValueTask> Wrap(Action action)
        => _ =>
        {
            action();
            return ValueTask.CompletedTask;
        };

    /// <inheritdoc />
    protected override ValueTask ExecuteCoreAsync(CancellationToken cancellationToken) => _execute(cancellationToken);

    /// <inheritdoc />
    protected override ValueTask UnExecuteCoreAsync(CancellationToken cancellationToken) => _unexecute(cancellationToken);
}
