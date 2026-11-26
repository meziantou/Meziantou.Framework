namespace Meziantou.Framework.Hashing;

/// <summary>Computes the BLAKE2b hash function (RFC 7693), with a digest of 1 to 64 bytes.</summary>
/// <remarks>
/// <para>
/// Use the static <see cref="HashData(ReadOnlySpan{byte}, int)"/> methods to hash data in one call, or an instance to append
/// the data in several calls. Keyed hashing (BLAKE2b used as a MAC) is not supported.
/// </para>
/// <para>Instances are not thread-safe.</para>
/// </remarks>
public sealed class Blake2b
{
    /// <summary>The maximum size of a digest, in bytes (RFC 7693 section 2.1, nn).</summary>
    public const int MaxHashSize = 64;

    private Blake2bHasher _hasher;

    /// <summary>Initializes a new instance of the <see cref="Blake2b"/> class.</summary>
    /// <param name="hashSize">The size of the digest, in bytes: 1 to <see cref="MaxHashSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSize"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public Blake2b(int hashSize = MaxHashSize)
    {
        _hasher = new Blake2bHasher(hashSize);
    }

    /// <summary>Gets the size of the digest, in bytes.</summary>
    public int HashSize => _hasher.HashSize;

    /// <summary>Computes the BLAKE2b digest of data.</summary>
    /// <param name="source">The data to hash.</param>
    /// <param name="hashSize">The size of the digest, in bytes: 1 to <see cref="MaxHashSize"/>.</param>
    /// <returns>The digest, of <paramref name="hashSize"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSize"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public static byte[] HashData(ReadOnlySpan<byte> source, int hashSize = MaxHashSize)
    {
        var hasher = new Blake2bHasher(hashSize);
        hasher.AppendData(source);
        var destination = new byte[hashSize];
        hasher.GetHashAndReset(destination);
        return destination;
    }

    /// <summary>Computes the BLAKE2b digest of data into a buffer.</summary>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer that receives the digest: its length, 1 to <see cref="MaxHashSize"/> bytes, is the size of the digest.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="destination"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public static void HashData(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length is < 1 or > MaxHashSize)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length, $"The length of the destination must be in the range 1 to {MaxHashSize}.");

        var hasher = new Blake2bHasher(destination.Length);
        hasher.AppendData(source);
        hasher.GetHashAndReset(destination);
    }

    /// <summary>Appends data to the message.</summary>
    /// <param name="data">The data to append.</param>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        _hasher.AppendData(data);
    }

    /// <summary>Computes the digest of the appended data, and restarts the computation of a new digest of the same size.</summary>
    /// <returns>The digest, of <see cref="HashSize"/> bytes.</returns>
    public byte[] GetHashAndReset()
    {
        var destination = new byte[_hasher.HashSize];
        _hasher.GetHashAndReset(destination);
        return destination;
    }

    /// <summary>Computes the digest of the appended data into a buffer, and restarts the computation of a new digest of the same size.</summary>
    /// <param name="destination">The buffer that receives the digest: its length must be <see cref="HashSize"/>.</param>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not <see cref="HashSize"/>.</exception>
    public void GetHashAndReset(Span<byte> destination)
    {
        _hasher.GetHashAndReset(destination);
    }
}
