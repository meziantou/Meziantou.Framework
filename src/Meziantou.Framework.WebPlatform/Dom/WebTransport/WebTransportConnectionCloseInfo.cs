namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The application error code and the reason of a WebTransport session closed cleanly.</summary>
/// <param name="CloseCode">The application error code.</param>
/// <param name="Reason">The reason, at most 1024 bytes in UTF-8.</param>
public sealed record WebTransportConnectionCloseInfo(uint CloseCode, string Reason);
