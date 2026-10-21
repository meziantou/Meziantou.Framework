namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A JavaScript exception: the .NET exception that carries a thrown JavaScript value.</summary>
public sealed class JsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JsException"/> class.</summary>
    public JsException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with a message.</summary>
    public JsException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with a message and an inner exception.</summary>
    public JsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsException"/> class with the thrown value.</summary>
    public JsException(JsValue value)
        : this(value, JsEngine.Current?.GetCurrentLocation())
    {
    }

    internal JsException(JsValue value, JsSourceLocation? location)
        : base(ErrorFormatting.GetDescription(value))
    {
        Value = value;
        Location = location;
    }

    /// <summary>Gets the thrown value.</summary>
    public JsValue Value { get; }

    /// <summary>Gets the position of the code that threw the exception, when it was thrown by JavaScript code.</summary>
    public JsSourceLocation? Location { get; }
}
