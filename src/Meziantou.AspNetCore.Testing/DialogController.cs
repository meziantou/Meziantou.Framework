using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// Answers the simple dialogs of pages (<c>alert()</c>, <c>confirm()</c>, <c>prompt()</c>, and the confirmation of a
/// <c>beforeunload</c> event) and records them in <see cref="History"/>. Each page has its own controller
/// (<see cref="TestPage.Dialogs"/>); set <see cref="TestPageSettings.Dialogs"/> (or <c>WebAppBrowserOptions.Dialogs</c>,
/// <c>ComponentTestHostOptions.Dialogs</c>) for pages to share one.
/// </summary>
/// <remarks>
/// <para>
/// The script waits for the answer: the handlers run on the event loop of the page, during the action that shows the
/// dialog, so the answers are set up before the action. A dialog is answered by the first of:
/// </para>
/// <list type="number">
/// <item><description>the next handler queued with <see cref="HandleNext(Action{PageDialog})"/>, <see cref="AcceptNext(string?)"/> or <see cref="DismissNext"/> (each one answers one dialog, in order),</description></item>
/// <item><description>the handler set with <see cref="HandleAll(Action{PageDialog}?)"/>, <see cref="AcceptAll(string?)"/> or <see cref="DismissAll"/>,</description></item>
/// <item><description>the <see cref="BrowsingContextOptions.UserPromptHandler"/> set with <see cref="TestPageSettings.ConfigureBrowsingContext"/>,</description></item>
/// <item><description>the answer of a browser that shows no dialog (<c>confirm()</c> returns false, <c>prompt()</c> returns null, the documents are unloaded). In <see cref="DialogMode.Strict"/> mode (the default), the action then fails with an <see cref="UnexpectedDialogException"/>.</description></item>
/// </list>
/// <para>
/// A handler that answers nothing lets the next one answer. An exception thrown by a handler (a failed assertion) fails the
/// action that shows the dialog, and the dialog gets the answer of a browser that shows no dialog.
/// </para>
/// </remarks>
public sealed class DialogController
{
    private readonly Lock _lock = new();
    private readonly Queue<Action<PageDialog>> _nextHandlers = [];
    private readonly List<PageDialog> _history = [];
    private Action<PageDialog>? _allHandler;

    /// <summary>Creates a controller that accepts every dialog.</summary>
    /// <returns>The controller.</returns>
    public static DialogController AcceptingAll()
    {
        var controller = new DialogController();
        controller.AcceptAll();
        return controller;
    }

    /// <summary>Creates a controller that dismisses every dialog.</summary>
    /// <returns>The controller.</returns>
    public static DialogController DismissingAll()
    {
        var controller = new DialogController();
        controller.DismissAll();
        return controller;
    }

    /// <summary>Gets or sets what the controller does with a dialog that no handler answers. The default is <see cref="DialogMode.Strict"/>.</summary>
    public DialogMode Mode { get; set; } = DialogMode.Strict;

    /// <summary>Gets the dialogs that the pages showed, in order, with their answers.</summary>
    /// <remarks>
    /// The dialogs that a browser does not show are not there (a document sandboxed without <c>allow-modals</c>, a
    /// document being unloaded): the script gets the answer of a browser that shows no dialog.
    /// </remarks>
    public IReadOnlyList<PageDialog> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>Gets the number of handlers queued with <see cref="HandleNext(Action{PageDialog})"/>, <see cref="AcceptNext(string?)"/> and <see cref="DismissNext"/> that no dialog used yet.</summary>
    public int PendingCount
    {
        get
        {
            lock (_lock)
            {
                return _nextHandlers.Count;
            }
        }
    }

    /// <summary>Queues a handler that answers the next dialog: it accepts or dismisses the dialog, and can check it.</summary>
    /// <param name="handler">The handler.</param>
    public void HandleNext(Action<PageDialog> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_lock)
        {
            _nextHandlers.Enqueue(handler);
        }
    }

    /// <summary>Accepts the next dialog (OK): <c>confirm()</c> returns true, <c>prompt()</c> returns the text, and the document of a <c>beforeunload</c> confirmation is unloaded.</summary>
    /// <param name="text">The text entered in a <c>prompt()</c> dialog, or null for its default value.</param>
    public void AcceptNext(string? text = null) => HandleNext(dialog => dialog.Accept(text));

    /// <summary>Dismisses the next dialog (Cancel): <c>confirm()</c> returns false, <c>prompt()</c> returns null, and the navigation or the closing of a <c>beforeunload</c> confirmation is canceled.</summary>
    public void DismissNext() => HandleNext(dialog => dialog.Dismiss());

    /// <summary>Sets the handler of the dialogs that no queued handler answers, or removes it.</summary>
    /// <param name="handler">The handler, or null.</param>
    public void HandleAll(Action<PageDialog>? handler)
    {
        lock (_lock)
        {
            _allHandler = handler;
        }
    }

    /// <summary>Accepts the dialogs that no queued handler answers.</summary>
    /// <param name="text">The text entered in the <c>prompt()</c> dialogs, or null for their default value.</param>
    public void AcceptAll(string? text = null) => HandleAll(dialog => dialog.Accept(text));

    /// <summary>Dismisses the dialogs that no queued handler answers.</summary>
    public void DismissAll() => HandleAll(dialog => dialog.Dismiss());

    /// <summary>Removes the dialogs of <see cref="History"/>.</summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }

    /// <summary>Answers a dialog of a page, on its event loop; the exceptions of the handlers are reported to the page.</summary>
    internal UserPromptResult Handle(TestPage page, CallbackContext context, UserPrompt prompt, Func<CallbackContext, UserPrompt, UserPromptResult>? fallback)
    {
        var dialog = new PageDialog(page, prompt);
        Action<PageDialog>? nextHandler;
        Action<PageDialog>? allHandler;
        lock (_lock)
        {
            _nextHandlers.TryDequeue(out nextHandler);
            allHandler = _allHandler;
        }

        try
        {
            nextHandler?.Invoke(dialog);
            if (!dialog.IsAnswered)
            {
                allHandler?.Invoke(dialog);
            }

            if (!dialog.IsAnswered && fallback is not null)
            {
                dialog.SetAnswer(fallback(context, prompt));
            }

            if (!dialog.IsAnswered)
            {
                dialog.SetAnswer(GetDefaultAnswer(prompt.Type), unexpected: true);
                if (Mode is DialogMode.Strict)
                {
                    page.ReportDialogFailure(new UnexpectedDialogException(dialog));
                }
            }
        }
        catch (Exception ex)
        {
            if (!dialog.IsAnswered)
            {
                dialog.SetAnswer(GetDefaultAnswer(prompt.Type), unexpected: true);
            }

            page.ReportDialogFailure(ex);
        }
        finally
        {
            dialog.Close();
        }

        lock (_lock)
        {
            _history.Add(dialog);
        }

        page.OnDialogShown(dialog);
        return dialog.Result!;
    }

    // The answers of a browser that shows no dialog (a null UserPromptHandler)
    private static UserPromptResult GetDefaultAnswer(UserPromptType type) => type is UserPromptType.Confirm or UserPromptType.Prompt ? UserPromptResult.Dismiss : UserPromptResult.Accept();
}
