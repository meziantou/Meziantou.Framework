namespace Meziantou.Framework.WebTransport.Http3;

// The error codes of HTTP/3 (RFC 9114, section 8.1), QPACK (RFC 9204, section 6), HTTP Datagrams (RFC 9297) and
// WebTransport over HTTP/3 (draft-ietf-webtrans-http3)
internal static class Http3ErrorCode
{
    public const long NoError = 0x100;
    public const long GeneralProtocolError = 0x101;
    public const long InternalError = 0x102;
    public const long StreamCreationError = 0x103;
    public const long ClosedCriticalStream = 0x104;
    public const long FrameUnexpected = 0x105;
    public const long FrameError = 0x106;
    public const long ExcessiveLoad = 0x107;
    public const long IdError = 0x108;
    public const long SettingsError = 0x109;
    public const long MissingSettings = 0x10A;
    public const long RequestRejected = 0x10B;
    public const long RequestCancelled = 0x10C;
    public const long RequestIncomplete = 0x10D;
    public const long MessageError = 0x10E;
    public const long ConnectError = 0x10F;
    public const long QPackDecompressionFailed = 0x200;
    public const long DatagramError = 0x33;

    public const long WebTransportBufferedStreamRejected = 0x3994BD84;
    public const long WebTransportSessionGone = 0x170D7B68;

    // The application error codes of WebTransport (32 bits) are mapped to a range of HTTP/3 error codes that skips the
    // reserved codes 0x1f * N + 0x21 (draft-ietf-webtrans-http3, section 4.4)
    public const long WebTransportApplicationErrorFirst = 0x52E4A40FA8DB;
    public const long WebTransportApplicationErrorLast = 0x52E5AC983162;

    public static long FromWebTransportErrorCode(uint errorCode) => WebTransportApplicationErrorFirst + errorCode + (errorCode / 0x1E);

    public static uint? ToWebTransportErrorCode(long http3ErrorCode)
    {
        if (http3ErrorCode is < WebTransportApplicationErrorFirst or > WebTransportApplicationErrorLast)
            return null;

        // A reserved code (0x1f * N + 0x21) is not a valid mapping
        if ((http3ErrorCode - 0x21) % 0x1F == 0)
            return null;

        var shifted = http3ErrorCode - WebTransportApplicationErrorFirst;
        return (uint)(shifted - (shifted / 0x1F));
    }
}
