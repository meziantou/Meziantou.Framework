namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>What failed: a stream or the session (https://w3c.github.io/webtransport/#enumdef-webtransporterrorsource).</summary>
/// <seealso href="https://w3c.github.io/webtransport/#enumdef-webtransporterrorsource"/>
[IdlInterface("WebTransportErrorSource", "https://w3c.github.io/webtransport/#enumdef-webtransporterrorsource")]
public enum WebTransportErrorSource
{
    /// <summary>A stream failed.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterrorsource-stream"/>
    [IdlMember("stream", "https://w3c.github.io/webtransport/#dom-webtransporterrorsource-stream")]
    Stream,

    /// <summary>The session failed.</summary>
    /// <seealso href="https://w3c.github.io/webtransport/#dom-webtransporterrorsource-session"/>
    [IdlMember("session", "https://w3c.github.io/webtransport/#dom-webtransporterrorsource-session")]
    Session,
}
