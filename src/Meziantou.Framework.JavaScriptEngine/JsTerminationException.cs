namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The exception thrown when the engine stops a script: a limit is exceeded or the execution is canceled. The scripts
/// cannot catch it (the catch and finally blocks do not run).
/// </summary>
public sealed class JsTerminationException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="JsTerminationException"/> class.</summary>
    public JsTerminationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsTerminationException"/> class with a message.</summary>
    public JsTerminationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsTerminationException"/> class with a message and an inner exception.</summary>
    public JsTerminationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsTerminationException"/> class with a reason.</summary>
    public JsTerminationException(JsTerminationReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>Gets why the script was stopped.</summary>
    public JsTerminationReason Reason { get; }
}
