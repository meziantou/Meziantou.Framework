using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The error of a <see cref="WebSocketStream"/> (https://whatpr.org/websockets/48.html#the-websocketerror-interface): a
/// <see cref="DomException"/> named <c>WebSocketError</c>, with the status code and the reason of the closing handshake. The
/// promises and the streams of a WebSocketStream are rejected with a WebSocketError when the connection cannot be
/// established or is not closed cleanly, and a WebSocketError given to <c>cancel()</c> or <c>abort()</c> of its streams
/// gives the status code and the reason of the Close frame.
/// </summary>
/// <seealso href="https://whatpr.org/websockets/48.html#websocketerror"/>
[IdlInterface("WebSocketError", "https://whatpr.org/websockets/48.html#websocketerror")]
[Experimental(ExperimentalApis.WebSocketStream, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class WebSocketError : DomException
{
    /// <summary>Creates an error with an empty message, without status code and with an empty reason.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror"/>
    [IdlMember("constructor", "https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror")]
    public WebSocketError()
        : this("", init: null)
    {
    }

    /// <summary>Creates an error with a message, without status code and with an empty reason.</summary>
    /// <param name="message">The message.</param>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror"/>
    [IdlMember("constructor", "https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror")]
    public WebSocketError(string? message)
        : this(message ?? "", init: null)
    {
    }

    /// <summary>
    /// Creates an error with a message, a status code and a reason
    /// (https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror): a reason without status code gives the
    /// <c>1000</c> status code.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="init">The status code (<c>1000</c> or a code from <c>3000</c> to <c>4999</c>) and the reason (at most 123 bytes in UTF-8).</param>
    /// <exception cref="DomException">An <c>InvalidAccessError</c> when the status code is not valid, or a <c>SyntaxError</c> when the reason is too long.</exception>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror"/>
    [IdlMember("constructor", "https://whatpr.org/websockets/48.html#dom-websocketerror-websocketerror")]
    public WebSocketError(string message, WebSocketCloseInfo? init)
        : base(message ?? "", "WebSocketError")
    {
        var code = init?.CloseCode;
        var reason = InfraString.ToScalarValueString(init?.Reason ?? "");
        ValidateCloseCodeAndReason(code, reason);
        if (code is null && reason.Length > 0)
        {
            code = 1000;
        }

        CloseCode = code;
        Reason = reason;
    }

    /// <summary>Creates a <c>WebSocketError</c> with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    [NotInIdl]
    public WebSocketError(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "WebSocketError";
        Reason = "";
    }

    /// <summary>Creates the error of a connection that is closed (its status code, e.g. <c>1006</c>, is not validated).</summary>
    internal WebSocketError(string message, ushort? closeCode, string reason)
        : base(message, "WebSocketError")
    {
        CloseCode = closeCode;
        Reason = reason;
    }

    /// <summary>Gets the status code of the closing handshake, or null.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketerror-closecode"/>
    [IdlMember("closeCode", "https://whatpr.org/websockets/48.html#dom-websocketerror-closecode")]
    public ushort? CloseCode { get; }

    /// <summary>Gets the reason of the closing handshake, or the empty string.</summary>
    /// <seealso href="https://whatpr.org/websockets/48.html#dom-websocketerror-reason"/>
    [IdlMember("reason", "https://whatpr.org/websockets/48.html#dom-websocketerror-reason")]
    public string Reason { get; }

    /// <summary>
    /// https://whatpr.org/websockets/48.html#validate-close-code-and-reason: the status code is <c>1000</c> or a code from
    /// <c>3000</c> to <c>4999</c>, and the reason is at most 123 bytes in UTF-8.
    /// </summary>
    internal static void ValidateCloseCodeAndReason(ushort? code, string? reason)
    {
        if (code is { } value && value != 1000 && value is not (>= 3000 and <= 4999))
            throw new DomException("The close code must be 1000 or between 3000 and 4999", "InvalidAccessError");

        if (reason is not null && Encoding.UTF8.GetByteCount(reason) > 123)
            throw DomException.Syntax("The close reason must not be longer than 123 bytes");
    }
}
