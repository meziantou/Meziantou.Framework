namespace Meziantou.Framework.NodeJs;

/// <summary>Represents an error raised by JavaScript code, or a failure of the Node.js process hosting it.</summary>
public sealed class NodeJsException : Exception
{
    /// <summary>Initializes a new instance of <see cref="NodeJsException"/>.</summary>
    public NodeJsException()
    {
    }

    /// <summary>Initializes a new instance of <see cref="NodeJsException"/> with the specified message.</summary>
    public NodeJsException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="NodeJsException"/> with the specified message and inner exception.</summary>
    public NodeJsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal NodeJsException(string message, string? javaScriptErrorName, string? javaScriptStack)
        : base(message)
    {
        JavaScriptErrorName = javaScriptErrorName;
        JavaScriptStack = javaScriptStack;
    }

    internal NodeJsException(string message, int? exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>Gets the name of the JavaScript error (e.g. <c>TypeError</c>), or <see langword="null"/> when the error does not come from JavaScript code.</summary>
    public string? JavaScriptErrorName { get; }

    /// <summary>Gets the JavaScript stack trace, if available.</summary>
    public string? JavaScriptStack { get; }

    /// <summary>Gets the exit code of the Node.js process when the error is caused by the process exiting.</summary>
    public int? ExitCode { get; }
}
