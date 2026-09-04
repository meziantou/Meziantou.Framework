namespace Meziantou.Framework.UndoRedo;

/// <summary>
/// Thrown when a <see cref="UndoRedoManager.HistoryChanged"/> handler fails. The operation that raised the
/// notification (recording, undoing, redoing or clearing) has already completed successfully, so the caller
/// must not retry it; only the notification failed.
/// </summary>
public sealed class UndoRedoNotificationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UndoRedoNotificationException"/> class.</summary>
    public UndoRedoNotificationException()
        : base("A HistoryChanged handler threw an exception. The undo/redo operation itself completed successfully.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UndoRedoNotificationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    public UndoRedoNotificationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UndoRedoNotificationException"/> class.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception thrown by the handler.</param>
    public UndoRedoNotificationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
