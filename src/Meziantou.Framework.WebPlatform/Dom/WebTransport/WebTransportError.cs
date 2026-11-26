namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The error of a WebTransport session or stream (https://w3c.github.io/webtransport/#web-transport-error-interface): a
/// <see cref="DomException"/> named <c>WebTransportError</c>, whose <see cref="ErrorSource"/> tells whether the session or a
/// stream failed, with the application error code of the stream the server aborted.
/// </summary>
/// <seealso href="https://w3c.github.io/webtransport/#webtransporterror"/>
[IdlInterface("WebTransportError", "https://w3c.github.io/webtransport/#webtransporterror")]
public sealed class WebTransportError : DomException
{
    /// <summary>Creates an error of a stream, with an empty message and without error code.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror"/>
    [IdlMember("constructor", "https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror")]
    public WebTransportError()
        : this("", options: null)
    {
    }

    /// <summary>Creates an error of a stream with a message, without error code.</summary>
    /// <param name="message">The message.</param>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror"/>
    [IdlMember("constructor", "https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror")]
    public WebTransportError(string? message)
        : this(message ?? "", options: null)
    {
    }

    /// <summary>Creates an error with a message, a source and an application error code (https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror).</summary>
    /// <param name="message">The message.</param>
    /// <param name="options">The source (<c>stream</c> by default) and the error code of the stream.</param>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror"/>
    [IdlMember("constructor", "https://w3c.github.io/webtransport/#dom-webtransporterror-webtransporterror")]
    public WebTransportError(string message, WebTransportErrorOptions? options)
        : base(message ?? "", "WebTransportError")
    {
        ErrorSource = options?.Source ?? WebTransportErrorSource.Stream;
        StreamErrorCode = options?.StreamErrorCode;
    }

    /// <summary>Creates a <c>WebTransportError</c> with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    [NotInIdl]
    public WebTransportError(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "WebTransportError";
    }

    /// <summary>Gets whether the session or a stream failed (the <c>source</c> attribute: <see cref="Exception.Source"/> is another member).</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterror-source"/>
    [IdlMember("source", "https://w3c.github.io/webtransport/#dom-webtransporterror-source")]
    public WebTransportErrorSource ErrorSource { get; }

    /// <summary>Gets the application error code of the stream the server aborted, or null.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterror-streamerrorcode"/>
    [IdlMember("streamErrorCode", "https://w3c.github.io/webtransport/#dom-webtransporterror-streamerrorcode")]
    public uint? StreamErrorCode { get; }
}
