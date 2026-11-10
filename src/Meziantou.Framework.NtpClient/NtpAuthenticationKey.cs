namespace Meziantou.Framework.Ntp;

/// <summary>
/// A symmetric key shared by an NTP client and an NTP server, which authenticates the packets they exchange with a
/// message authentication code (MAC), as defined by RFC 5905 and RFC 8573.
/// </summary>
/// <remarks>
/// The key identifier and the key must be the same on both sides. With ntpd and chrony, they are a line of the keys file:
/// <c>1 AES128CMAC 000102030405060708090a0b0c0d0e0f</c> is <c>new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.AesCmac, Convert.FromHexString("000102030405060708090a0b0c0d0e0f"))</c>.
/// </remarks>
public sealed class NtpAuthenticationKey
{
    /// <summary>The size, in bytes, of an AES-128-CMAC key.</summary>
    private const int AesCmacKeySize = 16;

    private readonly byte[] _key;

    /// <summary>Initializes a new instance of the <see cref="NtpAuthenticationKey"/> class.</summary>
    /// <param name="keyId">The key identifier, sent in every authenticated packet. It cannot be 0, which RFC 5905 reserves.</param>
    /// <param name="algorithm">The MAC algorithm.</param>
    /// <param name="key">The secret key. <see cref="NtpAuthenticationAlgorithm.AesCmac"/> requires exactly 16 bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="keyId"/> is 0, or <paramref name="algorithm"/> is not defined.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty, or does not have the size the algorithm requires.</exception>
    public NtpAuthenticationKey(uint keyId, NtpAuthenticationAlgorithm algorithm, ReadOnlySpan<byte> key)
    {
        ArgumentOutOfRangeException.ThrowIfZero(keyId);
        if (algorithm is not (NtpAuthenticationAlgorithm.AesCmac or NtpAuthenticationAlgorithm.Sha1 or NtpAuthenticationAlgorithm.Md5))
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "The algorithm is not supported.");

        if (key.IsEmpty)
            throw new ArgumentException("The key cannot be empty.", nameof(key));

        if (algorithm is NtpAuthenticationAlgorithm.AesCmac && key.Length is not AesCmacKeySize)
            throw new ArgumentException($"An AES-128-CMAC key must be {AesCmacKeySize} bytes, but was {key.Length} bytes.", nameof(key));

        KeyId = keyId;
        Algorithm = algorithm;
        _key = key.ToArray();
    }

    /// <summary>Gets the key identifier.</summary>
    public uint KeyId { get; }

    /// <summary>Gets the MAC algorithm.</summary>
    public NtpAuthenticationAlgorithm Algorithm { get; }

    /// <summary>Gets the secret key.</summary>
    public ReadOnlyMemory<byte> Key => _key;
}
