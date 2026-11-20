using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// A simple dialog that a document of a page shows (<c>alert()</c>, <c>confirm()</c>, <c>prompt()</c>, or the confirmation
/// of a <c>beforeunload</c> event). The handlers of a <see cref="DialogController"/> answer it with
/// <see cref="Accept(string?)"/> or <see cref="Dismiss"/>; <see cref="DialogController.History"/> keeps it with its answer.
/// </summary>
public sealed class PageDialog
{
    private readonly Lock _lock = new();
    private bool _canAnswer = true;

    internal PageDialog(TestPage page, UserPrompt prompt)
    {
        Page = page;
        Type = prompt.Type;
        Message = prompt.Message;
        DefaultValue = prompt.DefaultValue;
    }

    /// <summary>Gets the page that shows the dialog.</summary>
    public TestPage Page { get; }

    /// <summary>Gets the kind of the dialog.</summary>
    public UserPromptType Type { get; }

    /// <summary>Gets the message of the dialog, with its newlines normalized to <c>\n</c> (empty for <see cref="UserPromptType.BeforeUnload"/>).</summary>
    public string Message { get; }

    /// <summary>Gets the default value of the text of a <see cref="UserPromptType.Prompt"/> dialog, else the empty string.</summary>
    public string DefaultValue { get; }

    /// <summary>Gets whether the dialog has an answer.</summary>
    public bool IsAnswered { get; private set; }

    /// <summary>
    /// Gets whether the dialog was accepted (OK), rather than dismissed (Cancel): the value that <c>confirm()</c> returns, and
    /// whether the document of a <see cref="UserPromptType.BeforeUnload"/> dialog is unloaded.
    /// </summary>
    public bool Accepted { get; private set; }

    /// <summary>
    /// Gets the text of an accepted <see cref="UserPromptType.Prompt"/> dialog: the text of the answer, or
    /// <see cref="DefaultValue"/> when the answer has none. It is null when the dialog is dismissed (<c>prompt()</c> returns
    /// null) or is not a <see cref="UserPromptType.Prompt"/> dialog.
    /// </summary>
    public string? Text { get; private set; }

    /// <summary>Gets whether no handler answered the dialog: it got the answer of a browser that shows no dialog.</summary>
    public bool IsUnexpected { get; private set; }

    internal UserPromptResult? Result { get; private set; }

    /// <summary>Accepts the dialog (OK): <c>confirm()</c> returns true, <c>prompt()</c> returns the text, and the document of a <c>beforeunload</c> confirmation is unloaded.</summary>
    /// <param name="text">The text entered in a <see cref="UserPromptType.Prompt"/> dialog, or null for its default value. The other dialogs ignore it.</param>
    /// <exception cref="InvalidOperationException">The dialog already has an answer, or its handler returned.</exception>
    public void Accept(string? text = null) => SetAnswer(UserPromptResult.Accept(Type is UserPromptType.Prompt ? text : null));

    /// <summary>Dismisses the dialog (Cancel): <c>confirm()</c> returns false, <c>prompt()</c> returns null, and the navigation or the closing of a <c>beforeunload</c> confirmation is canceled.</summary>
    /// <exception cref="InvalidOperationException">The dialog already has an answer, or its handler returned.</exception>
    public void Dismiss() => SetAnswer(UserPromptResult.Dismiss);

    /// <inheritdoc />
    public override string ToString() => $"{Type}: {Message}";

    internal void SetAnswer(UserPromptResult result, bool unexpected = false)
    {
        lock (_lock)
        {
            if (!_canAnswer)
                throw new InvalidOperationException("The dialog was answered: a handler answers a dialog once, before it returns.");

            if (IsAnswered)
                throw new InvalidOperationException($"The dialog is already {(Accepted ? "accepted" : "dismissed")}.");

            Result = result;
            IsAnswered = true;
            IsUnexpected = unexpected;
            Accepted = result.Accepted;
            Text = Type is UserPromptType.Prompt && result.Accepted ? result.Text ?? DefaultValue : null;
        }
    }

    // The script continues: the handlers cannot answer anymore
    internal void Close()
    {
        lock (_lock)
        {
            _canAnswer = false;
        }
    }
}
