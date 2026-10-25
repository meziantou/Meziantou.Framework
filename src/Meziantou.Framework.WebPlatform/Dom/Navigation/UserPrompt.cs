namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kind of a simple dialog (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#simple-dialogs).</summary>
public enum UserPromptType
{
    /// <summary>A message, shown by <c>alert()</c>: the user can only acknowledge it.</summary>
    Alert,

    /// <summary>A question, asked by <c>confirm()</c>: the user accepts (OK) or dismisses (Cancel) it.</summary>
    Confirm,

    /// <summary>A question, asked by <c>prompt()</c>: the user enters a text and accepts it, or dismisses the dialog.</summary>
    Prompt,

    /// <summary>
    /// The confirmation of the unloading of a document whose <c>beforeunload</c> event is canceled
    /// (https://html.spec.whatwg.org/multipage/browsing-the-web.html#checking-if-unloading-is-canceled): the document is unloaded
    /// when the user accepts it, and the navigation or the closing is canceled when the user dismisses it.
    /// </summary>
    BeforeUnload,
}

/// <summary>A simple dialog that a document shows to the user (see <see cref="BrowsingContextOptions.UserPromptHandler"/>).</summary>
public sealed class UserPrompt
{
    internal UserPrompt(UserPromptType type, string message, string defaultValue)
    {
        Type = type;
        Message = message;
        DefaultValue = defaultValue;
    }

    /// <summary>Gets the kind of the dialog.</summary>
    public UserPromptType Type { get; }

    /// <summary>Gets the message of the dialog (empty for <see cref="UserPromptType.BeforeUnload"/>: the browsers do not show the message of the document).</summary>
    public string Message { get; }

    /// <summary>Gets the default value of the text of a <see cref="UserPromptType.Prompt"/> dialog, else the empty string.</summary>
    public string DefaultValue { get; }
}

/// <summary>The answer of the user to a simple dialog (see <see cref="BrowsingContextOptions.UserPromptHandler"/>).</summary>
public sealed class UserPromptResult
{
    private UserPromptResult(bool accepted, string? text)
    {
        Accepted = accepted;
        Text = text;
    }

    /// <summary>Gets whether the user accepted the dialog (OK), rather than dismissed it (Cancel).</summary>
    public bool Accepted { get; }

    /// <summary>Gets the text entered in a <see cref="UserPromptType.Prompt"/> dialog that is accepted, or null for the default value of the dialog.</summary>
    public string? Text { get; }

    /// <summary>Gets the result of a dialog that the user dismissed.</summary>
    public static UserPromptResult Dismiss { get; } = new(accepted: false, text: null);

    /// <summary>Returns the result of a dialog that the user accepted.</summary>
    /// <param name="text">The text entered in a <see cref="UserPromptType.Prompt"/> dialog, or null for its default value.</param>
    public static UserPromptResult Accept(string? text = null) => text is null ? AcceptDefault : new(accepted: true, text);

    private static readonly UserPromptResult AcceptDefault = new(accepted: true, text: null);
}
