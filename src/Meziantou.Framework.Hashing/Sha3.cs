namespace Meziantou.Framework.Hashing;

/// <summary>Computes the SHA-3 hash functions SHA3-224, SHA3-256, SHA3-384 and SHA3-512 (FIPS 202).</summary>
/// <remarks>
/// The implementation is fully managed: unlike the SHA-3 types of <c>System.Security.Cryptography</c>, it does not depend on
/// the cryptographic library of the operating system, so it is available on every platform (including macOS).
/// </remarks>
public static class Sha3
{
    /// <summary>Computes the SHA-3 digest of data.</summary>
    /// <param name="hashSizeInBits">The size of the digest, in bits: 224, 256, 384 or 512.</param>
    /// <param name="source">The data to hash.</param>
    /// <returns>The digest, of <paramref name="hashSizeInBits"/> / 8 bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSizeInBits"/> is not 224, 256, 384 or 512.</exception>
    public static byte[] HashData(int hashSizeInBits, ReadOnlySpan<byte> source)
    {
        var destination = new byte[Keccak.GetSha3Length(hashSizeInBits)];
        Keccak.Sha3(hashSizeInBits, source, destination);
        return destination;
    }

    /// <summary>Computes the SHA-3 digest of data into a buffer.</summary>
    /// <param name="hashSizeInBits">The size of the digest, in bits: 224, 256, 384 or 512.</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer that receives the digest: its length must be <paramref name="hashSizeInBits"/> / 8 bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSizeInBits"/> is not 224, 256, 384 or 512.</exception>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not the size of the digest.</exception>
    public static void HashData(int hashSizeInBits, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Keccak.Sha3(hashSizeInBits, source, destination);
    }
}
