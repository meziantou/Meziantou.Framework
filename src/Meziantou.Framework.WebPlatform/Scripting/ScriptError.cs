namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The information of an exception reported by the library
/// (https://html.spec.whatwg.org/multipage/webappapis.html#extract-error): the attributes of the <see cref="Dom.ErrorEvent"/>
/// fired at the window.
/// </summary>
public sealed class ScriptError
{
    /// <summary>Creates the information of an exception.</summary>
    /// <param name="message">The message of the error.</param>
    /// <param name="error">The exception value (the <c>error</c> attribute of the <see cref="Dom.ErrorEvent"/>): a value of the script engine, or a .NET exception.</param>
    public ScriptError(string message, object? error)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
        Error = error;
    }

    /// <summary>Gets the message of the error.</summary>
    public string Message { get; }

    /// <summary>Gets the exception value: a value of the script engine, or a .NET exception. It is null for a muted error (the <c>error</c> attribute of the <see cref="Dom.ErrorEvent"/> is then null).</summary>
    public object? Error { get; }

    /// <summary>Gets the URL of the script where the error occurred, or an empty string.</summary>
    public string Filename { get; init; } = "";

    /// <summary>Gets the line number where the error occurred (1-based), or 0.</summary>
    public int LineNumber { get; init; }

    /// <summary>Gets the column number where the error occurred (1-based), or 0.</summary>
    public int ColumnNumber { get; init; }

    /// <summary>Returns the information of a muted error (https://html.spec.whatwg.org/multipage/webappapis.html#muted-errors).</summary>
    internal static ScriptError Muted() => new("Script error.", error: null);

    /// <inheritdoc />
    public override string ToString() => Filename.Length == 0 ? Message : $"{Message} ({Filename}:{LineNumber}:{ColumnNumber})";
}
