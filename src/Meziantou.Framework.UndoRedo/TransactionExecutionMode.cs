namespace Meziantou.Framework.UndoRedo;

/// <summary>Determines when the actions recorded in an <see cref="UndoRedoTransaction"/> are executed.</summary>
public enum TransactionExecutionMode
{
    /// <summary>The actions are executed when the transaction is committed. Reading the model between two recordings shows the state from before the transaction.</summary>
    Deferred,

    /// <summary>The actions are executed as they are recorded, so the model stays up to date while the transaction is open.</summary>
    Immediate,
}
