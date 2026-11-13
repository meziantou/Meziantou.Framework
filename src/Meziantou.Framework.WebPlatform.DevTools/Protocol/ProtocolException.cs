namespace Meziantou.Framework.WebPlatform.DevTools.Protocol;

/// <summary>An error of a command of the protocol: the client receives its code and its message (<c>{ "error": { "code": ..., "message": ... } }</c>).</summary>
#pragma warning disable CA1064 // The errors of the protocol are reported to the clients, never thrown to the application
internal sealed class ProtocolException : Exception
#pragma warning restore CA1064
{
    /// <summary>The code of the errors of the commands (<c>kServerError</c> of Chromium).</summary>
    public const int ServerError = -32000;

    /// <summary>The code of the commands that do not exist.</summary>
    public const int MethodNotFound = -32601;

    /// <summary>The code of the invalid parameters.</summary>
    public const int InvalidParams = -32602;

    public ProtocolException()
        : this("Internal error")
    {
    }

    public ProtocolException(string message)
        : this(message, ServerError)
    {
    }

    public ProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = ServerError;
    }

    public ProtocolException(string message, int code)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }

    public static ProtocolException NotFound(string method) => new("'" + method + "' wasn't found", MethodNotFound);

    public static ProtocolException InvalidParameters(string message) => new("Invalid parameters: " + message, InvalidParams);
}
