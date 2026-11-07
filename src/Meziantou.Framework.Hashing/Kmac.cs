namespace Meziantou.Framework.Hashing;

/// <summary>Computes the message authentication codes KMAC128 and KMAC256 (NIST SP 800-185).</summary>
/// <remarks>
/// The requested length is part of the input of KMAC: a shorter code is not a prefix of a longer one. The implementation is
/// fully managed: it does not depend on the cryptographic library of the operating system, so it is available on every
/// platform.
/// </remarks>
public static class Kmac
{
    /// <summary>Computes the KMAC of data.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (KMAC128) or 256 (KMAC256).</param>
    /// <param name="key">The key K.</param>
    /// <param name="source">The data X to authenticate.</param>
    /// <param name="outputLength">The length L of the code, in bytes.</param>
    /// <param name="customization">The customization string S.</param>
    /// <returns>The code, of <paramref name="outputLength"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256, or <paramref name="outputLength"/> is negative.</exception>
    public static byte[] HashData(int securityStrength, ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, int outputLength, ReadOnlySpan<byte> customization = default)
    {
        var destination = Keccak.AllocateOutput(outputLength);
        Keccak.Kmac(securityStrength, key, source, destination, customization);
        return destination;
    }

    /// <summary>Computes the KMAC of data into a buffer.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (KMAC128) or 256 (KMAC256).</param>
    /// <param name="key">The key K.</param>
    /// <param name="source">The data X to authenticate.</param>
    /// <param name="destination">The buffer that receives the code: its length is the length L of the code.</param>
    /// <param name="customization">The customization string S.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256.</exception>
    public static void HashData(int securityStrength, ReadOnlySpan<byte> key, ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> customization = default)
    {
        Keccak.Kmac(securityStrength, key, source, destination, customization);
    }
}
