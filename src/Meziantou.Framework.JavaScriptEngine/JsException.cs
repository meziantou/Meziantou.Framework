namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A JavaScript exception: the .NET exception that carries a thrown JavaScript value.</summary>
/// <remarks>
/// The constructors that take a message (or nothing) throw a JavaScript <c>Error</c> whose <c>message</c> is the message of
/// the exception: the error is created in the current realm the first time <see cref="Value"/> is read (e.g. when a script
/// catches it). Use <see cref="JsRealm.CreateException"/> to throw another type of error.
/// </remarks>
public sealed class JsException : Exception
{
    private JsValue _value;

    // The exception was created from a message: Value is an Error created on first use
    private bool _isValuePending;

    // The exception was created with a message (the message of the parameterless constructor is not the one of the error)
    private readonly bool _hasMessage;

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class: it throws an <c>Error</c> without message.</summary>
    public JsException()
    {
        _isValuePending = true;
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with a message: it throws an <c>Error</c> with this message.</summary>
    public JsException(string message)
        : base(message)
    {
        _isValuePending = true;
        _hasMessage = true;
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with a message and an inner exception: it throws an <c>Error</c> with this message.</summary>
    public JsException(string message, Exception innerException)
        : base(message, innerException)
    {
        _isValuePending = true;
        _hasMessage = true;
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with the thrown value.</summary>
    public JsException(JsValue value)
        : this(value, JsEngine.Current?.GetCurrentLocation())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JsException"/> class with the thrown value and the position of the code
    /// that threw it (e.g. the position of a syntax error).
    /// </summary>
    public JsException(JsValue value, JsSourceLocation? location)
        : base(ErrorFormatting.GetDescription(value))
    {
        _value = value;
        Location = location;
    }

    /// <summary>
    /// Gets the thrown value. For an exception created with a message, it is an <c>Error</c> of the current realm, created on
    /// first access (undefined when no engine is entered on the thread at that time).
    /// </summary>
    public JsValue Value
    {
        get
        {
            if (_isValuePending && JsEngine.Current?.CurrentRealm is { } realm)
            {
                _isValuePending = false;
                _value = new JsValue(realm.CreateError(ErrorType.Error, _hasMessage ? Message : ""));
            }

            return _value;
        }
    }

    /// <summary>Gets the position of the code that threw the exception, when it was thrown by JavaScript code.</summary>
    public JsSourceLocation? Location { get; }

    /// <summary>
    /// Gets the JavaScript frames of the stack trace of the thrown error, one per line (<c>    at f (file.js:1:2)</c>), the
    /// innermost first: the <c>stack</c> of the error without its first line (see <see cref="JsObject.GetErrorStack"/>).
    /// The frames are the ones of the code that created the error, which can differ from the code that threw it. It is
    /// null when the thrown value is not an error object with a stack trace (e.g. <c>throw 42</c>), and empty when the error
    /// was created without JavaScript code on the stack.
    /// </summary>
    public string? JavaScriptStackTrace
    {
        get
        {
            if (Value.Cell is not JsObject obj || obj.GetErrorStack() is not { } stack)
                return null;

            var index = stack.IndexOf("\n    at ", StringComparison.Ordinal);
            return index < 0 ? "" : stack[(index + 1)..];
        }
    }

    /// <summary>Returns the description of the exception, followed by its JavaScript stack trace when it has one.</summary>
    public override string ToString()
    {
        var text = base.ToString();
        if (string.IsNullOrEmpty(JavaScriptStackTrace))
            return text;

        return text + Environment.NewLine + "JavaScript stack trace:" + Environment.NewLine + JavaScriptStackTrace;
    }
}
