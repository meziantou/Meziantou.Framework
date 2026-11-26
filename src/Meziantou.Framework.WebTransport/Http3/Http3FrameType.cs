namespace Meziantou.Framework.WebTransport.Http3;

// The frame types of HTTP/3 (RFC 9114, section 7.2) and the signal of a WebTransport bidirectional stream
internal static class Http3FrameType
{
    public const long Data = 0x0;
    public const long Headers = 0x1;
    public const long CancelPush = 0x3;
    public const long Settings = 0x4;
    public const long PushPromise = 0x5;
    public const long GoAway = 0x7;
    public const long MaxPushId = 0xD;
    public const long WebTransportStream = 0x41;

    // HTTP/2 frame types that are reserved in HTTP/3: receiving one is a connection error (RFC 9114, section 7.2.8)
    public static bool IsReservedHttp2Type(long type) => type is 0x2 or 0x6 or 0x8 or 0x9;
}
