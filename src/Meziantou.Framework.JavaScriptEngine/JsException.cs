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
}
