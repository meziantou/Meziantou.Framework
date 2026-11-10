namespace Meziantou.Framework.Ntp;

/// <summary>
/// The algorithm of the message authentication code (MAC) of an <see cref="NtpAuthenticationKey"/>.
/// </summary>
public enum NtpAuthenticationAlgorithm
{
    /// <summary>
    /// AES-128-CMAC, as defined by RFC 8573. The key must be 16 bytes. Prefer it to the other algorithms.
    /// </summary>
    AesCmac,

    /// <summary>
    /// SHA-1 of the key followed by the packet, as defined by RFC 5905. Kept for interoperability with older servers.
    /// </summary>
    Sha1,

    /// <summary>
    /// MD5 of the key followed by the packet, as defined by RFC 5905. RFC 8573 deprecates it: use it only with a server
    /// that supports nothing else.
    /// </summary>
    Md5,
}
