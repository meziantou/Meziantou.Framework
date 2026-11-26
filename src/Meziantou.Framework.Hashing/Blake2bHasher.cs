using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Hashing;

/// <summary>
/// The state of a BLAKE2b computation (RFC 7693): the chaining value, the buffered last block and the counter. Used by
/// <see cref="Blake2b"/> and by Argon2. The struct is mutable: it must be passed by reference.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Blake2bHasher
{
    // The size of a block, in bytes (RFC 7693 section 2.1, bb)
    public const int BlockSize = 128;

    // https://www.rfc-editor.org/rfc/rfc7693#section-2.6: the initialization vector, the same as SHA-512
    private const ulong IV0 = 0x6A09E667F3BCC908UL;
    private const ulong IV1 = 0xBB67AE8584CAA73BUL;
    private const ulong IV2 = 0x3C6EF372FE94F82BUL;
    private const ulong IV3 = 0xA54FF53A5F1D36F1UL;
    private const ulong IV4 = 0x510E527FADE682D1UL;
    private const ulong IV5 = 0x9B05688C2B3E6C1FUL;
    private const ulong IV6 = 0x1F83D9ABFB41BD6BUL;
    private const ulong IV7 = 0x5BE0CD19137E2179UL;

    private ChainingValue _state;
    private Block _buffer;
    private ulong _counterLow;
    private ulong _counterHigh;
    private int _bufferLength;
    private int _hashSize;

    /// <summary>Starts the computation of a digest of <paramref name="hashSize"/> bytes.</summary>
    public Blake2bHasher(int hashSize)
    {
        Reset(hashSize);
    }

    public readonly int HashSize => _hashSize;

    /// <summary>Restarts the computation of a digest of <paramref name="hashSize"/> bytes (RFC 7693 section 3.3, the parameter block).</summary>
    public void Reset(int hashSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(hashSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hashSize, Blake2b.MaxHashSize);

        _hashSize = hashSize;

        // Digest length nn, key length kk = 0, fanout = 1, depth = 1
        _state[0] = IV0 ^ 0x01010000UL ^ (ulong)hashSize;
        _state[1] = IV1;
        _state[2] = IV2;
        _state[3] = IV3;
        _state[4] = IV4;
        _state[5] = IV5;
        _state[6] = IV6;
        _state[7] = IV7;
        _bufferLength = 0;
        _counterLow = 0;
        _counterHigh = 0;
    }

    /// <summary>Appends data to the message.</summary>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return;

        // The last block is compressed by GetHashAndReset (with the final block flag): a full block is compressed only when
        // more data follows
        Span<byte> buffer = _buffer;
        var bufferLength = _bufferLength;
        if (bufferLength != 0)
        {
            var count = Math.Min(BlockSize - bufferLength, data.Length);
            data[..count].CopyTo(buffer[bufferLength..]);
            data = data[count..];
            if (data.IsEmpty)
            {
                _bufferLength = bufferLength + count;
                return;
            }

            IncrementCounter(BlockSize);
            Compress(ref _state, buffer, _counterLow, _counterHigh, finalBlockFlag: 0);
        }

        // The blocks of the data are compressed where they are, without copying them to the buffer
        while (data.Length > BlockSize)
        {
            IncrementCounter(BlockSize);
            Compress(ref _state, data, _counterLow, _counterHigh, finalBlockFlag: 0);
            data = data[BlockSize..];
        }

        data.CopyTo(buffer);
        _bufferLength = data.Length;
    }

    /// <summary>Appends the 32-bit little-endian representation of <paramref name="value"/> to the message (LE32 in RFC 9106).</summary>
    public void AppendUInt32LittleEndian(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AppendData(bytes);
    }

    /// <summary>Computes the digest into <paramref name="destination"/>, whose length must be <see cref="HashSize"/>, and restarts the computation of a new digest of the same size.</summary>
    public void GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length != _hashSize)
        {
            throw new ArgumentException("The destination must have the length of the digest.", nameof(destination));
        }

        IncrementCounter(_bufferLength);
        Span<byte> buffer = _buffer;
        buffer[_bufferLength..].Clear();
        Compress(ref _state, buffer, _counterLow, _counterHigh, finalBlockFlag: ulong.MaxValue);

        Span<byte> digest = stackalloc byte[Blake2b.MaxHashSize];
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

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.2: the compression function F of the first 128 bytes of data. The state and
    // the message are in locals and the 12 rounds are unrolled, so that the message schedule SIGMA (section 2.7) is resolved
    // at compile time: rounds 10 and 11 use the permutations of rounds 0 and 1.
    private static void Compress(ref ChainingValue state, ReadOnlySpan<byte> data, ulong counterLow, ulong counterHigh, ulong finalBlockFlag)
    {
        var block = data[..BlockSize];
        var m0 = BinaryPrimitives.ReadUInt64LittleEndian(block);
        var m1 = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        var m2 = BinaryPrimitives.ReadUInt64LittleEndian(block[16..]);
        var m3 = BinaryPrimitives.ReadUInt64LittleEndian(block[24..]);
        var m4 = BinaryPrimitives.ReadUInt64LittleEndian(block[32..]);
        var m5 = BinaryPrimitives.ReadUInt64LittleEndian(block[40..]);
        var m6 = BinaryPrimitives.ReadUInt64LittleEndian(block[48..]);
        var m7 = BinaryPrimitives.ReadUInt64LittleEndian(block[56..]);
        var m8 = BinaryPrimitives.ReadUInt64LittleEndian(block[64..]);
        var m9 = BinaryPrimitives.ReadUInt64LittleEndian(block[72..]);
        var m10 = BinaryPrimitives.ReadUInt64LittleEndian(block[80..]);
        var m11 = BinaryPrimitives.ReadUInt64LittleEndian(block[88..]);
        var m12 = BinaryPrimitives.ReadUInt64LittleEndian(block[96..]);
        var m13 = BinaryPrimitives.ReadUInt64LittleEndian(block[104..]);
        var m14 = BinaryPrimitives.ReadUInt64LittleEndian(block[112..]);
        var m15 = BinaryPrimitives.ReadUInt64LittleEndian(block[120..]);

        var v0 = state[0];
        var v1 = state[1];
        var v2 = state[2];
        var v3 = state[3];
        var v4 = state[4];
        var v5 = state[5];
        var v6 = state[6];
        var v7 = state[7];
        var v8 = IV0;
        var v9 = IV1;
        var v10 = IV2;
        var v11 = IV3;
        var v12 = IV4 ^ counterLow;
        var v13 = IV5 ^ counterHigh;
        var v14 = IV6 ^ finalBlockFlag;
        var v15 = IV7;

        // Round 0
        Mix(ref v0, ref v4, ref v8, ref v12, m0, m1);
        Mix(ref v1, ref v5, ref v9, ref v13, m2, m3);
        Mix(ref v2, ref v6, ref v10, ref v14, m4, m5);
        Mix(ref v3, ref v7, ref v11, ref v15, m6, m7);
        Mix(ref v0, ref v5, ref v10, ref v15, m8, m9);
        Mix(ref v1, ref v6, ref v11, ref v12, m10, m11);
        Mix(ref v2, ref v7, ref v8, ref v13, m12, m13);
        Mix(ref v3, ref v4, ref v9, ref v14, m14, m15);

        // Round 1
        Mix(ref v0, ref v4, ref v8, ref v12, m14, m10);
        Mix(ref v1, ref v5, ref v9, ref v13, m4, m8);
        Mix(ref v2, ref v6, ref v10, ref v14, m9, m15);
        Mix(ref v3, ref v7, ref v11, ref v15, m13, m6);
        Mix(ref v0, ref v5, ref v10, ref v15, m1, m12);
        Mix(ref v1, ref v6, ref v11, ref v12, m0, m2);
        Mix(ref v2, ref v7, ref v8, ref v13, m11, m7);
        Mix(ref v3, ref v4, ref v9, ref v14, m5, m3);

        // Round 2
        Mix(ref v0, ref v4, ref v8, ref v12, m11, m8);
        Mix(ref v1, ref v5, ref v9, ref v13, m12, m0);
        Mix(ref v2, ref v6, ref v10, ref v14, m5, m2);
        Mix(ref v3, ref v7, ref v11, ref v15, m15, m13);
        Mix(ref v0, ref v5, ref v10, ref v15, m10, m14);
        Mix(ref v1, ref v6, ref v11, ref v12, m3, m6);
        Mix(ref v2, ref v7, ref v8, ref v13, m7, m1);
        Mix(ref v3, ref v4, ref v9, ref v14, m9, m4);

        // Round 3
        Mix(ref v0, ref v4, ref v8, ref v12, m7, m9);
        Mix(ref v1, ref v5, ref v9, ref v13, m3, m1);
        Mix(ref v2, ref v6, ref v10, ref v14, m13, m12);
        Mix(ref v3, ref v7, ref v11, ref v15, m11, m14);
        Mix(ref v0, ref v5, ref v10, ref v15, m2, m6);
        Mix(ref v1, ref v6, ref v11, ref v12, m5, m10);
        Mix(ref v2, ref v7, ref v8, ref v13, m4, m0);
        Mix(ref v3, ref v4, ref v9, ref v14, m15, m8);

        // Round 4
        Mix(ref v0, ref v4, ref v8, ref v12, m9, m0);
        Mix(ref v1, ref v5, ref v9, ref v13, m5, m7);
        Mix(ref v2, ref v6, ref v10, ref v14, m2, m4);
        Mix(ref v3, ref v7, ref v11, ref v15, m10, m15);
        Mix(ref v0, ref v5, ref v10, ref v15, m14, m1);
        Mix(ref v1, ref v6, ref v11, ref v12, m11, m12);
        Mix(ref v2, ref v7, ref v8, ref v13, m6, m8);
        Mix(ref v3, ref v4, ref v9, ref v14, m3, m13);

        // Round 5
        Mix(ref v0, ref v4, ref v8, ref v12, m2, m12);
        Mix(ref v1, ref v5, ref v9, ref v13, m6, m10);
        Mix(ref v2, ref v6, ref v10, ref v14, m0, m11);
        Mix(ref v3, ref v7, ref v11, ref v15, m8, m3);
        Mix(ref v0, ref v5, ref v10, ref v15, m4, m13);
        Mix(ref v1, ref v6, ref v11, ref v12, m7, m5);
        Mix(ref v2, ref v7, ref v8, ref v13, m15, m14);
        Mix(ref v3, ref v4, ref v9, ref v14, m1, m9);

        // Round 6
        Mix(ref v0, ref v4, ref v8, ref v12, m12, m5);
        Mix(ref v1, ref v5, ref v9, ref v13, m1, m15);
        Mix(ref v2, ref v6, ref v10, ref v14, m14, m13);
        Mix(ref v3, ref v7, ref v11, ref v15, m4, m10);
        Mix(ref v0, ref v5, ref v10, ref v15, m0, m7);
        Mix(ref v1, ref v6, ref v11, ref v12, m6, m3);
        Mix(ref v2, ref v7, ref v8, ref v13, m9, m2);
        Mix(ref v3, ref v4, ref v9, ref v14, m8, m11);

        // Round 7
        Mix(ref v0, ref v4, ref v8, ref v12, m13, m11);
        Mix(ref v1, ref v5, ref v9, ref v13, m7, m14);
        Mix(ref v2, ref v6, ref v10, ref v14, m12, m1);
        Mix(ref v3, ref v7, ref v11, ref v15, m3, m9);
        Mix(ref v0, ref v5, ref v10, ref v15, m5, m0);
        Mix(ref v1, ref v6, ref v11, ref v12, m15, m4);
        Mix(ref v2, ref v7, ref v8, ref v13, m8, m6);
        Mix(ref v3, ref v4, ref v9, ref v14, m2, m10);

        // Round 8
        Mix(ref v0, ref v4, ref v8, ref v12, m6, m15);
        Mix(ref v1, ref v5, ref v9, ref v13, m14, m9);
        Mix(ref v2, ref v6, ref v10, ref v14, m11, m3);
        Mix(ref v3, ref v7, ref v11, ref v15, m0, m8);
        Mix(ref v0, ref v5, ref v10, ref v15, m12, m2);
        Mix(ref v1, ref v6, ref v11, ref v12, m13, m7);
        Mix(ref v2, ref v7, ref v8, ref v13, m1, m4);
        Mix(ref v3, ref v4, ref v9, ref v14, m10, m5);

        // Round 9
        Mix(ref v0, ref v4, ref v8, ref v12, m10, m2);
        Mix(ref v1, ref v5, ref v9, ref v13, m8, m4);
        Mix(ref v2, ref v6, ref v10, ref v14, m7, m6);
        Mix(ref v3, ref v7, ref v11, ref v15, m1, m5);
        Mix(ref v0, ref v5, ref v10, ref v15, m15, m11);
        Mix(ref v1, ref v6, ref v11, ref v12, m9, m14);
        Mix(ref v2, ref v7, ref v8, ref v13, m3, m12);
        Mix(ref v3, ref v4, ref v9, ref v14, m13, m0);

        // Round 10
        Mix(ref v0, ref v4, ref v8, ref v12, m0, m1);
        Mix(ref v1, ref v5, ref v9, ref v13, m2, m3);
        Mix(ref v2, ref v6, ref v10, ref v14, m4, m5);
        Mix(ref v3, ref v7, ref v11, ref v15, m6, m7);
        Mix(ref v0, ref v5, ref v10, ref v15, m8, m9);
        Mix(ref v1, ref v6, ref v11, ref v12, m10, m11);
        Mix(ref v2, ref v7, ref v8, ref v13, m12, m13);
        Mix(ref v3, ref v4, ref v9, ref v14, m14, m15);

        // Round 11
        Mix(ref v0, ref v4, ref v8, ref v12, m14, m10);
        Mix(ref v1, ref v5, ref v9, ref v13, m4, m8);
        Mix(ref v2, ref v6, ref v10, ref v14, m9, m15);
        Mix(ref v3, ref v7, ref v11, ref v15, m13, m6);
        Mix(ref v0, ref v5, ref v10, ref v15, m1, m12);
        Mix(ref v1, ref v6, ref v11, ref v12, m0, m2);
        Mix(ref v2, ref v7, ref v8, ref v13, m11, m7);
        Mix(ref v3, ref v4, ref v9, ref v14, m5, m3);

        state[0] ^= v0 ^ v8;
        state[1] ^= v1 ^ v9;
        state[2] ^= v2 ^ v10;
        state[3] ^= v3 ^ v11;
        state[4] ^= v4 ^ v12;
        state[5] ^= v5 ^ v13;
        state[6] ^= v6 ^ v14;
        state[7] ^= v7 ^ v15;
    }

    // https://www.rfc-editor.org/rfc/rfc7693#section-3.1: the mixing function G
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref ulong a, ref ulong b, ref ulong c, ref ulong d, ulong x, ulong y)
    {
        a += b + x;
        d = BitOperations.RotateRight(d ^ a, 32);
        c += d;
        b = BitOperations.RotateRight(b ^ c, 24);
        a += b + y;
        d = BitOperations.RotateRight(d ^ a, 16);
        c += d;
        b = BitOperations.RotateRight(b ^ c, 63);
    }

    /// <summary>The chaining value h[0..7].</summary>
    [InlineArray(8)]
    private struct ChainingValue
    {
        private ulong _element0;
    }

    /// <summary>The buffer of the last block of data, which is compressed with the final block flag.</summary>
    [InlineArray(BlockSize)]
    private struct Block
    {
        private byte _element0;
    }
}
