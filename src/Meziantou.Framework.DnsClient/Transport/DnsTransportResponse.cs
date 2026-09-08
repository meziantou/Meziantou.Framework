namespace Meziantou.Framework.DnsClient.Transport;

/// <summary>
/// A DNS response as it came off a transport, together with how long that transport reports the message has already
/// been sitting in a cache. Only DNS over HTTPS can report a non-zero age (RFC 8484 5.1: the DNS TTLs of a response
/// served from an HTTP cache must be reduced by the HTTP <c>Age</c> header).
/// </summary>
/// <param name="Data">The raw DNS message.</param>
/// <param name="AgeInSeconds">The number of seconds the message has been cached, or 0 when it comes straight from the server.</param>
internal readonly record struct DnsTransportResponse(byte[] Data, uint AgeInSeconds)
{
    public DnsTransportResponse(byte[] data)
        : this(data, AgeInSeconds: 0)
    {
    }
}
