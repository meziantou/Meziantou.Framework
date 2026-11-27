namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The options of a <see cref="WebTransportError"/> (https://w3c.github.io/webtransport/#dictdef-webtransporterroroptions).</summary>
/// <seealso href="https://w3c.github.io/webtransport/#dictdef-webtransporterroroptions"/>
[IdlInterface("WebTransportErrorOptions", "https://w3c.github.io/webtransport/#dictdef-webtransporterroroptions")]
public sealed class WebTransportErrorOptions
{
    /// <summary>Gets whether the session or a stream failed.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterroroptions-source"/>
    [IdlMember("source", "https://w3c.github.io/webtransport/#dom-webtransporterroroptions-source")]
    public WebTransportErrorSource Source { get; init; } = WebTransportErrorSource.Stream;

    /// <summary>Gets the application error code of the stream, or null.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterroroptions-streamerrorcode"/>
    [IdlMember("streamErrorCode", "https://w3c.github.io/webtransport/#dom-webtransporterroroptions-streamerrorcode")]
    public uint? StreamErrorCode { get; init; }
}
