namespace Meziantou.Framework.WebTransport.Http3;

// The identifiers of the SETTINGS frame
internal static class Http3SettingsIdentifier
{
    public const long QPackMaxTableCapacity = 0x01;
    public const long MaxFieldSectionSize = 0x06;
    public const long QPackBlockedStreams = 0x07;

    // RFC 8441 and RFC 9220
    public const long EnableConnectProtocol = 0x08;

    // RFC 9297, and the value of draft-ietf-masque-h3-datagram-04 that older implementations use
    public const long H3Datagram = 0x33;
    public const long H3DatagramDraft04 = 0xFFD277;

    // draft-ietf-webtrans-http3: SETTINGS_ENABLE_WEBTRANSPORT (drafts 02 to 06), SETTINGS_WEBTRANSPORT_MAX_SESSIONS
    // (drafts 07 and 08), SETTINGS_WT_MAX_SESSIONS (draft 09 and later)
    public const long EnableWebTransport = 0x2B603742;
    public const long WebTransportMaxSessionsDraft07 = 0xC671706A;
    public const long WebTransportMaxSessions = 0x14E9CD29;

    // HTTP/2 settings that are reserved in HTTP/3: receiving one is a connection error (RFC 9114, section 7.2.4.1)
    public static bool IsReservedHttp2Setting(long identifier) => identifier is 0x00 or 0x02 or 0x03 or 0x04 or 0x05;
}
