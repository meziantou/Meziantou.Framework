namespace Meziantou.AspNetCore.Testing;

/// <summary>What a <see cref="DialogController"/> does with a dialog that no handler answers.</summary>
public enum DialogMode
{
    /// <summary>
    /// The dialog gets the answer of a browser that shows no dialog, and the action that showed it fails with an
    /// <see cref="UnexpectedDialogException"/>.
    /// </summary>
    Strict,

    /// <summary>
    /// The dialog gets the answer of a browser that shows no dialog: <c>confirm()</c> returns false, <c>prompt()</c>
    /// returns null, and the documents are unloaded.
    /// </summary>
    Loose,
}
