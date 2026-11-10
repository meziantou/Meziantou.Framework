using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Locates, computes and verifies the message authentication code (MAC) that ends an authenticated NTP packet
/// (RFC 5905 section 7.3, RFC 7822, RFC 8573).
/// </summary>
internal static class NtpMac
{
    public const int HeaderSize = 48;
    public const int KeyIdSize = 4;

    /// <summary>A crypto-NAK is a MAC made of a key identifier and no digest: the peer could not authenticate the packet.</summary>
    public const int CryptoNakSize = KeyIdSize;

    /// <summary>The largest MAC: a key identifier and a SHA-1 digest. Anything longer after the header is an extension field (RFC 7822).</summary>
    public const int MaxMacSize = KeyIdSize + 20;

    private const int MinExtensionFieldSize = 16;

    public static int GetDigestSize(NtpAuthenticationAlgorithm algorithm) => algorithm switch
    {
        NtpAuthenticationAlgorithm.AesCmac => NtpAesCmac.Size,
        NtpAuthenticationAlgorithm.Sha1 => 20,
        NtpAuthenticationAlgorithm.Md5 => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "The algorithm is not supported."),
    };

    public static int GetMacSize(NtpAuthenticationKey key) => KeyIdSize + GetDigestSize(key.Algorithm);

    /// <summary>
    /// Skips the header and the extension fields of a packet, and returns where its MAC starts. The MAC is what remains:
    /// nothing, a crypto-NAK, or a key identifier and a 16 or 20-byte digest.
    /// </summary>
    /// <returns><see langword="false"/> when the packet is shorter than a header, an extension field is malformed, or what remains is not a MAC.</returns>
    public static bool TryGetMacOffset(ReadOnlySpan<byte> packet, out int macOffset)
    {
        macOffset = 0;
        if (packet.Length < HeaderSize)
            return false;

        var offset = HeaderSize;
        while (packet.Length - offset > MaxMacSize)
        {
            int length = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 2, 2));
            if (length < MinExtensionFieldSize || length % 4 is not 0 || length > packet.Length - offset)
                return false;

            offset += length;
        }

        if (packet.Length - offset is not (0 or CryptoNakSize or KeyIdSize + 16 or KeyIdSize + 20))
            return false;

        macOffset = offset;
        return true;
    }

    /// <summary>Writes the key identifier and the digest of <c>packet[..macOffset]</c> at <paramref name="macOffset"/>.</summary>
    public static void Write(NtpAuthenticationKey key, Span<byte> packet, int macOffset)
    {
        BinaryPrimitives.WriteUInt32BigEndian(packet.Slice(macOffset, KeyIdSize), key.KeyId);
        ComputeDigest(key, packet[..macOffset], packet.Slice(macOffset + KeyIdSize, GetDigestSize(key.Algorithm)));
    }

    /// <summary>Checks that the packet ends with a MAC computed with <paramref name="key"/>, in constant time.</summary>
    public static bool Verify(NtpAuthenticationKey key, ReadOnlySpan<byte> packet, int macOffset)
    {
        var digestSize = GetDigestSize(key.Algorithm);
        if (packet.Length - macOffset != KeyIdSize + digestSize)
            return false;

        if (BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(macOffset, KeyIdSize)) != key.KeyId)
            return false;

        Span<byte> expected = stackalloc byte[20];
        expected = expected[..digestSize];
        ComputeDigest(key, packet[..macOffset], expected);

        return CryptographicOperations.FixedTimeEquals(expected, packet.Slice(macOffset + KeyIdSize, digestSize));
    }

    [SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 5905 defines the SHA-1 MAC; the caller chooses it to interoperate with servers that support nothing else")]
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "RFC 5905 defines the MD5 MAC; the caller chooses it to interoperate with servers that support nothing else")]
    private static void ComputeDigest(NtpAuthenticationKey key, ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (key.Algorithm is NtpAuthenticationAlgorithm.AesCmac)
        {
            NtpAesCmac.Compute(key.Key.Span, data, destination);
            return;
        }

        // RFC 5905: the digest of the key followed by the packet
        using var hash = IncrementalHash.CreateHash(key.Algorithm is NtpAuthenticationAlgorithm.Sha1 ? HashAlgorithmName.SHA1 : HashAlgorithmName.MD5);
        hash.AppendData(key.Key.Span);
        hash.AppendData(data);
        hash.GetHashAndReset(destination);
    }
}
