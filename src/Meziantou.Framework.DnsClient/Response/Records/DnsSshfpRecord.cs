namespace Meziantou.Framework.DnsClient.Response.Records;

/// <summary>
/// Represents a DNS SSHFP record for SSH key fingerprints (RFC 4255).
/// </summary>
public sealed class DnsSshfpRecord : DnsRecord
{
    /// <summary>Gets the algorithm number (1=RSA, 2=DSS, 3=ECDSA, 4=Ed25519, 6=Ed448).</summary>
    public byte Algorithm { get; internal set; }

    /// <summary>Gets the fingerprint type (1=SHA-1, 2=SHA-256).</summary>
    public byte FingerprintType { get; internal set; }

    /// <summary>Gets the fingerprint data.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
    public byte[] Fingerprint { get; internal set; } = [];
}
