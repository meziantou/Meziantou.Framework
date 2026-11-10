namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a TSIG pseudo-record (RFC 8945), the signature of a message. When <see cref="DnsClientOptions.TsigKey"/>
/// is set, the client verifies it before returning the response.
/// </summary>
public sealed class DnsTsigRecord : DnsRecord
{
    /// <summary>Gets the name of the HMAC algorithm (for instance <c>hmac-sha256</c>).</summary>
    public string Algorithm { get; internal set; } = "";

    /// <summary>Gets the time the message was signed, as seconds since Unix epoch.</summary>
    public ulong TimeSigned { get; internal set; }

    /// <summary>Gets the number of seconds of clock difference the signer allows.</summary>
    public ushort Fudge { get; internal set; }

    /// <summary>Gets the message authentication code.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Mac { get; internal set; } = [];

    /// <summary>Gets the identifier of the message when it was signed.</summary>
    public ushort OriginalId { get; internal set; }

    /// <summary>Gets the TSIG error (<see cref="DnsResponseCode.BadSignature"/>, <see cref="DnsResponseCode.BadKey"/>, <see cref="DnsResponseCode.BadTime"/>, ...), or <see cref="DnsResponseCode.NoError"/>.</summary>
    public DnsResponseCode Error { get; internal set; }

    /// <summary>Gets the other data, the time of the server in a <see cref="DnsResponseCode.BadTime"/> response.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] OtherData { get; internal set; } = [];

    /// <summary>Gets the offset of the record in the message, where the digest of a response stops.</summary>
    internal int MessageOffset { get; set; }
}
