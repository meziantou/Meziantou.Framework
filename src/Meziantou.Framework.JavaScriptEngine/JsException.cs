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
        : base(ErrorFormatting.GetDescription(value))
    {
        Value = value;
    }

    /// <summary>Gets the thrown value.</summary>
    public JsValue Value { get; }
}
