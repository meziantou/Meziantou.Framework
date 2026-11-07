using System.Buffers.Binary;
using System.Numerics;

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
    // The size of a block, in bytes (RFC 7693 section 2.1, bb)
    private const int BlockSize = 128;

    /// <summary>The maximum size of a digest, in bytes (RFC 7693 section 2.1, nn).</summary>
    public const int MaxHashSize = 64;

    // https://www.rfc-editor.org/rfc/rfc7693#section-2.6: the initialization vector, the same as SHA-512
    private static readonly ulong[] InitializationVector =
    [
        0x6A09E667F3BCC908UL, 0xBB67AE8584CAA73BUL, 0x3C6EF372FE94F82BUL, 0xA54FF53A5F1D36F1UL,
        0x510E527FADE682D1UL, 0x9B05688C2B3E6C1FUL, 0x1F83D9ABFB41BD6BUL, 0x5BE0CD19137E2179UL,
    ];

    // https://www.rfc-editor.org/rfc/rfc7693#section-2.7: the message word schedule; rounds 10 and 11 use the rows 0 and 1
    private static readonly byte[] Sigma =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
        14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3,
        11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4,
        7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8,
        9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13,
        2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9,
        12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11,
        13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10,
        6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5,
        10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0,
    ];

    private readonly ulong[] _state = new ulong[8];
    private readonly ulong[] _message = new ulong[16];
    private readonly ulong[] _work = new ulong[16];
    private readonly byte[] _buffer = new byte[BlockSize];
    private int _bufferLength;
    private ulong _counterLow;
    private ulong _counterHigh;
    private int _hashSize;

    /// <summary>Initializes a new instance of the <see cref="Blake2b"/> class.</summary>
    /// <param name="hashSize">The size of the digest, in bytes: 1 to <see cref="MaxHashSize"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSize"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public Blake2b(int hashSize = MaxHashSize)
    {
        Reset(hashSize);
    }

    /// <summary>Gets the size of the digest, in bytes.</summary>
    public int HashSize => _hashSize;

    /// <summary>Computes the BLAKE2b digest of data.</summary>
    /// <param name="source">The data to hash.</param>
    /// <param name="hashSize">The size of the digest, in bytes: 1 to <see cref="MaxHashSize"/>.</param>
    /// <returns>The digest, of <paramref name="hashSize"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashSize"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public static byte[] HashData(ReadOnlySpan<byte> source, int hashSize = MaxHashSize)
    {
        var blake2b = new Blake2b(hashSize);
        blake2b.AppendData(source);
        return blake2b.GetHashAndReset();
    }

    /// <summary>Computes the BLAKE2b digest of data into a buffer.</summary>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer that receives the digest: its length, 1 to <see cref="MaxHashSize"/> bytes, is the size of the digest.</param>
    /// <exception cref="ArgumentOutOfRangeException">The length of <paramref name="destination"/> is not in the range 1 to <see cref="MaxHashSize"/>.</exception>
    public static void HashData(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length is < 1 or > MaxHashSize)
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length, $"The length of the destination must be in the range 1 to {MaxHashSize}.");

        var blake2b = new Blake2b(destination.Length);
        blake2b.AppendData(source);
        blake2b.GetHashAndReset(destination);
    }

    /// <summary>Restarts the computation of a digest of <paramref name="hashSize"/> bytes (RFC 7693 section 3.3, the parameter block).</summary>
    internal void Reset(int hashSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hashSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hashSize, MaxHashSize);

        _hashSize = hashSize;
        InitializationVector.CopyTo(_state, 0);

        // Digest length nn, key length kk = 0, fanout = 1, depth = 1
        _state[0] ^= 0x01010000UL ^ (ulong)hashSize;
        _bufferLength = 0;
        _counterLow = 0;
        _counterHigh = 0;
    }

    /// <summary>Appends data to the message.</summary>
    /// <param name="data">The data to append.</param>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            // The last block is compressed by GetHashAndReset (with the final block flag): a full buffer is compressed only when more data follows
            if (_bufferLength == BlockSize)
            {
                IncrementCounter(BlockSize);
                Compress(_buffer, isFinalBlock: false);
                _bufferLength = 0;
            }

            var length = Math.Min(BlockSize - _bufferLength, data.Length);
            data[..length].CopyTo(_buffer.AsSpan(_bufferLength));
            _bufferLength += length;
            data = data[length..];
        }
    }

    /// <summary>Appends the 32-bit little-endian representation of <paramref name="value"/> to the message (LE32 in RFC 9106).</summary>
    internal void AppendUInt32LittleEndian(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AppendData(bytes);
    }

    /// <summary>Computes the digest of the appended data, and restarts the computation of a new digest of the same size.</summary>
    /// <returns>The digest, of <see cref="HashSize"/> bytes.</returns>
    public byte[] GetHashAndReset()
    {
        var destination = new byte[_hashSize];
        GetHashAndReset(destination);
        return destination;
    }

    /// <summary>Computes the digest of the appended data into a buffer, and restarts the computation of a new digest of the same size.</summary>
    /// <param name="destination">The buffer that receives the digest: its length must be <see cref="HashSize"/>.</param>
    /// <exception cref="ArgumentException">The length of <paramref name="destination"/> is not <see cref="HashSize"/>.</exception>
    public void GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length != _hashSize)
        {
            throw new ArgumentException("The destination must have the length of the digest.", nameof(destination));
        }

        IncrementCounter(_bufferLength);
        _buffer.AsSpan(_bufferLength).Clear();
        Compress(_buffer, isFinalBlock: true);

        Span<byte> digest = stackalloc byte[MaxHashSize];
        for (var i = 0; i < 8; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(digest[(i * 8)..], _state[i]);
        }

        digest[..destination.Length].CopyTo(destination);
        Reset(_hashSize);
    }

    private void IncrementCounter(int length)
    {
        _counterLow += (ulong)length;
        if (_counterLow < (ulong)length)
        {
            _counterHigh++;
        }
    }

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.2: the compression function F
    private void Compress(ReadOnlySpan<byte> block, bool isFinalBlock)
    {
        var m = _message;
        for (var i = 0; i < 16; i++)
        {
            m[i] = BinaryPrimitives.ReadUInt64LittleEndian(block[(i * 8)..]);
        }

        var v = _work;
        _state.CopyTo(v, 0);
        InitializationVector.CopyTo(v, 8);
        v[12] ^= _counterLow;
        v[13] ^= _counterHigh;
        if (isFinalBlock)
        {
            v[14] = ~v[14];
        }

        for (var round = 0; round < 12; round++)
        {
            var s = Sigma.AsSpan((round % 10) * 16, 16);
            Mix(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
            Mix(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
            Mix(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
            Mix(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
            Mix(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
            Mix(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
            Mix(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
            Mix(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
        }

        for (var i = 0; i < 8; i++)
        {
            _state[i] ^= v[i] ^ v[i + 8];
        }
    }

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.1: the mixing function G
    private static void Mix(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
    {
        v[a] = v[a] + v[b] + x;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 32);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 24);
        v[a] = v[a] + v[b] + y;
        v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
        v[c] += v[d];
        v[b] = BitOperations.RotateRight(v[b] ^ v[c], 63);
    }
}
