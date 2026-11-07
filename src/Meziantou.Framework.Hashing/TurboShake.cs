namespace Meziantou.Framework.Hashing;

/// <summary>Computes the extendable-output functions TurboSHAKE128 and TurboSHAKE256 (RFC 9861).</summary>
/// <remarks>TurboSHAKE is the sponge of SHAKE with 12 rounds of the permutation instead of 24, which makes it about twice as fast.</remarks>
public static class TurboShake
{
    /// <summary>The default domain separation byte D.</summary>
    public const byte DefaultDomainSeparation = 0x1F;

    /// <summary>Computes the output of TurboSHAKE for data.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (TurboSHAKE128) or 256 (TurboSHAKE256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="outputLength">The length of the output, in bytes.</param>
    /// <param name="domainSeparation">The domain separation byte D, in the range 0x01 to 0x7F.</param>
    /// <returns>The output, of <paramref name="outputLength"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256, <paramref name="outputLength"/> is negative, or <paramref name="domainSeparation"/> is not in the range 0x01 to 0x7F.</exception>
    public static byte[] HashData(int securityStrength, ReadOnlySpan<byte> source, int outputLength, byte domainSeparation = DefaultDomainSeparation)
    {
        var destination = Keccak.AllocateOutput(outputLength);
        Keccak.TurboShake(securityStrength, source, destination, domainSeparation);
        return destination;
    }

    /// <summary>Computes the output of TurboSHAKE for data into a buffer.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (TurboSHAKE128) or 256 (TurboSHAKE256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer to fill with the output.</param>
    /// <param name="domainSeparation">The domain separation byte D, in the range 0x01 to 0x7F.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256, or <paramref name="domainSeparation"/> is not in the range 0x01 to 0x7F.</exception>
    public static void HashData(int securityStrength, ReadOnlySpan<byte> source, Span<byte> destination, byte domainSeparation = DefaultDomainSeparation)
    {
        Keccak.TurboShake(securityStrength, source, destination, domainSeparation);
    }
}
