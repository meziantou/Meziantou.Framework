namespace Meziantou.Framework.WebTransport.Http3;

// The types of the unidirectional streams (RFC 9114, section 6.2; RFC 9204, section 4.2; draft-ietf-webtrans-http3)
internal static class Http3StreamType
{
    public const long Control = 0x00;
    public const long Push = 0x01;
    public const long QPackEncoder = 0x02;
    public const long QPackDecoder = 0x03;
    public const long WebTransport = 0x54;
}
