namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A page showed a dialog that no handler of its <see cref="DialogController"/> answered, in <see cref="DialogMode.Strict"/>
/// mode. Answer it before the action that shows it (<see cref="DialogController.AcceptNext(string?)"/>,
/// <see cref="DialogController.DismissNext"/>, <see cref="DialogController.HandleNext(Action{PageDialog})"/>), or answer
/// every dialog (<see cref="DialogController.AcceptAll(string?)"/>, <see cref="DialogController.DismissAll"/>).
/// </summary>
public sealed class UnexpectedDialogException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="UnexpectedDialogException"/> class.</summary>
    public UnexpectedDialogException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnexpectedDialogException"/> class.</summary>
    /// <param name="message">The message.</param>
    public UnexpectedDialogException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="UnexpectedDialogException"/> class.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public UnexpectedDialogException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal UnexpectedDialogException(PageDialog dialog)
        : base($"The page showed an unexpected {dialog.Type} dialog: \"{dialog.Message}\". Answer it before the action that shows it, with the {nameof(DialogController)} of the page ({nameof(TestPage)}.{nameof(TestPage.Dialogs)}), or set its {nameof(DialogController.Mode)} to {nameof(DialogMode.Loose)}.")
    {
        Dialog = dialog;
    }

    /// <summary>Gets the dialog, or null.</summary>
    public PageDialog? Dialog { get; }
}
