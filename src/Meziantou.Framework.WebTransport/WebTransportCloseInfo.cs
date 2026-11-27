namespace Meziantou.Framework.WebTransport;

/// <summary>The application error code and reason of a closed session (the CLOSE_WEBTRANSPORT_SESSION capsule).</summary>
/// <param name="ErrorCode">The application error code.</param>
/// <param name="Reason">The reason, at most 1024 bytes in UTF-8.</param>
public sealed record WebTransportCloseInfo(uint ErrorCode, string Reason);
