namespace Meziantou.Framework.Hashing;

/// <summary>Computes the extendable-output functions KT128 and KT256 (KangarooTwelve, RFC 9861).</summary>
/// <remarks>KangarooTwelve is a tree hash over TurboSHAKE: the data is split in chunks of 8 KiB, whose hashes are combined.</remarks>
public static class KangarooTwelve
{
    /// <summary>Computes the output of KangarooTwelve for data.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (KT128) or 256 (KT256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="outputLength">The length of the output, in bytes.</param>
    /// <param name="customization">The customization string C.</param>
    /// <returns>The output, of <paramref name="outputLength"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256, or <paramref name="outputLength"/> is negative.</exception>
    public static byte[] HashData(int securityStrength, ReadOnlySpan<byte> source, int outputLength, ReadOnlySpan<byte> customization = default)
    {
        var destination = Keccak.AllocateOutput(outputLength);
        Keccak.KangarooTwelve(securityStrength, source, destination, customization);
        return destination;
    }

    /// <summary>Computes the output of KangarooTwelve for data into a buffer.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (KT128) or 256 (KT256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer to fill with the output.</param>
    /// <param name="customization">The customization string C.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256.</exception>
    public static void HashData(int securityStrength, ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> customization = default)
    {
        Keccak.KangarooTwelve(securityStrength, source, destination, customization);
    }
}
