namespace Meziantou.Framework.WebTransport.Http3;

// The SETTINGS frame (RFC 9114, section 7.2.4)
internal sealed class Http3Settings
{
    private readonly Dictionary<long, long> _values = [];

    public long? Get(long identifier) => _values.TryGetValue(identifier, out var value) ? value : null;

    public bool SupportsWebTransport =>
        Get(Http3SettingsIdentifier.EnableConnectProtocol) == 1
        && (Get(Http3SettingsIdentifier.H3Datagram) == 1 || Get(Http3SettingsIdentifier.H3DatagramDraft04) == 1)
        && (Get(Http3SettingsIdentifier.EnableWebTransport) == 1 || Get(Http3SettingsIdentifier.WebTransportMaxSessionsDraft07) > 0 || Get(Http3SettingsIdentifier.WebTransportMaxSessions) > 0);

    public static Http3Settings Parse(ReadOnlySpan<byte> payload)
    {
        var settings = new Http3Settings();
        while (!payload.IsEmpty)
        {
            if (!VariableLengthInteger.TryRead(payload, out var identifier, out var identifierLength) || !VariableLengthInteger.TryRead(payload[identifierLength..], out var value, out var valueLength))
                throw new Http3ProtocolException(Http3ErrorCode.FrameError, "Truncated SETTINGS frame.");

            if (Http3SettingsIdentifier.IsReservedHttp2Setting(identifier))
                throw new Http3ProtocolException(Http3ErrorCode.SettingsError, $"The setting 0x{identifier:X} is reserved.");

            if (!settings._values.TryAdd(identifier, value))
                throw new Http3ProtocolException(Http3ErrorCode.SettingsError, $"The setting 0x{identifier:X} is duplicated.");

            // The Boolean settings are 0 or 1 (RFC 9220, section 3; RFC 9297, section 2.1.1)
            if (identifier is Http3SettingsIdentifier.EnableConnectProtocol or Http3SettingsIdentifier.H3Datagram && value > 1)
                throw new Http3ProtocolException(Http3ErrorCode.SettingsError, $"The value {value} of the Boolean setting 0x{identifier:X} is not 0 or 1.");

            payload = payload[(identifierLength + valueLength)..];
        }

        return settings;
    }

    // The settings of this endpoint: no dynamic table, extended CONNECT, HTTP datagrams and WebTransport with the
    // identifiers of the drafts whose features are implemented. SETTINGS_WT_MAX_SESSIONS (draft 09 and later) is not sent:
    // it enables the flow control of the sessions (WT_MAX_DATA, WT_MAX_STREAMS...), which is not implemented, and whose
    // initial limits would be 0. maxSessions is the number of sessions the peer can open on the connection.
    public static byte[] EncodeLocalSettings(int maxFieldSectionSize, int maxSessions)
    {
        var payload = new List<byte>();
        void Add(long identifier, long value)
        {
            VariableLengthInteger.Write(payload, identifier);
            VariableLengthInteger.Write(payload, value);
        }

        Add(Http3SettingsIdentifier.MaxFieldSectionSize, maxFieldSectionSize);
        Add(Http3SettingsIdentifier.EnableConnectProtocol, 1);
        Add(Http3SettingsIdentifier.H3Datagram, 1);
        Add(Http3SettingsIdentifier.H3DatagramDraft04, 1);
        Add(Http3SettingsIdentifier.EnableWebTransport, 1);
        Add(Http3SettingsIdentifier.WebTransportMaxSessionsDraft07, maxSessions);
        return Http3FrameReader.EncodeFrame(Http3FrameType.Settings, [.. payload]);
    }
}
