using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// BLAKE2sp, the hash of the content of RAR5 entries: 8 BLAKE2s leaves (RFC 7693) that hash the interleaved 64-byte
/// blocks of the data, and a root BLAKE2s that hashes the 8 leaf digests. The digest is 32 bytes.
/// </summary>
internal sealed class Blake2sp
{
    public const int HashSize = 32;
    private const int BlockSize = 64;
    private const int Leaves = 8;

    private readonly Blake2s[] _leaves = new Blake2s[Leaves];
    private int _stripeOffset;

    public Blake2sp()
    {
        for (var i = 0; i < Leaves; i++)
        {
            _leaves[i] = new Blake2s(nodeOffset: (uint)i, nodeDepth: 0, lastNode: i == Leaves - 1);
        }
    }

    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var hash = new Blake2sp();
        hash.Append(data);
        return hash.GetHash();
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var leaf = _stripeOffset / BlockSize;
            var count = Math.Min(data.Length, BlockSize - (_stripeOffset % BlockSize));
            _leaves[leaf].Append(data[..count]);
            data = data[count..];
            _stripeOffset = (_stripeOffset + count) % (BlockSize * Leaves);
        }
    }

    public byte[] GetHash()
    {
        var root = new Blake2s(nodeOffset: 0, nodeDepth: 1, lastNode: true);
        Span<byte> digest = stackalloc byte[HashSize];
        foreach (var leaf in _leaves)
        {
            leaf.Finish(digest);
            root.Append(digest);
        }

        var result = new byte[HashSize];
        root.Finish(result);
        return result;
    }

    /// <summary>A BLAKE2s node of the BLAKE2sp tree (fanout 8, depth 2, inner length 32).</summary>
    private sealed class Blake2s
    {
        private static readonly uint[] Iv = [0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19];

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

        private readonly uint[] _h = new uint[8];
        private readonly byte[] _buffer = new byte[BlockSize];
        private readonly bool _lastNode;
        private int _bufferLength;
        private ulong _counter;

        public Blake2s(uint nodeOffset, byte nodeDepth, bool lastNode)
        {
            _lastNode = lastNode;
            Iv.CopyTo(_h, 0);

            // Parameter block: digest length 32, key length 0, fanout 8, depth 2, leaf length 0, node offset, node depth, inner length 32
            _h[0] ^= 32u | (8u << 16) | (2u << 24);
            _h[2] ^= nodeOffset;
            _h[3] ^= ((uint)nodeDepth << 16) | (32u << 24);
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            while (!data.IsEmpty)
            {
                // The last block is kept until more data come: it is compressed with the final flag
                if (_bufferLength == BlockSize)
                {
                    _counter += BlockSize;
                    Compress(_buffer, final: false);
                    _bufferLength = 0;
                }

                var count = Math.Min(data.Length, BlockSize - _bufferLength);
                data[..count].CopyTo(_buffer.AsSpan(_bufferLength));
                _bufferLength += count;
                data = data[count..];
            }
        }

        public void Finish(Span<byte> digest)
        {
            _counter += (ulong)_bufferLength;
            _buffer.AsSpan(_bufferLength).Clear();
            Compress(_buffer, final: true);
            for (var i = 0; i < 8; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(digest[(i * 4)..], _h[i]);
            }
        }

        private void Compress(ReadOnlySpan<byte> block, bool final)
        {
            Span<uint> m = stackalloc uint[16];
            for (var i = 0; i < 16; i++)
            {
                m[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * 4)..]);
            }

            Span<uint> v = stackalloc uint[16];
            _h.CopyTo(v);
            Iv.CopyTo(v[8..]);
            v[12] ^= (uint)_counter;
            v[13] ^= (uint)(_counter >> 32);
            if (final)
            {
                v[14] = ~v[14];
                if (_lastNode)
                {
                    v[15] = ~v[15];
                }
            }

            for (var round = 0; round < 10; round++)
            {
                var s = Sigma.AsSpan(round * 16, 16);
                G(v, 0, 4, 8, 12, m[s[0]], m[s[1]]);
                G(v, 1, 5, 9, 13, m[s[2]], m[s[3]]);
                G(v, 2, 6, 10, 14, m[s[4]], m[s[5]]);
                G(v, 3, 7, 11, 15, m[s[6]], m[s[7]]);
                G(v, 0, 5, 10, 15, m[s[8]], m[s[9]]);
                G(v, 1, 6, 11, 12, m[s[10]], m[s[11]]);
                G(v, 2, 7, 8, 13, m[s[12]], m[s[13]]);
                G(v, 3, 4, 9, 14, m[s[14]], m[s[15]]);
            }

            for (var i = 0; i < 8; i++)
            {
                _h[i] ^= v[i] ^ v[i + 8];
            }
        }

        private static void G(Span<uint> v, int a, int b, int c, int d, uint x, uint y)
        {
            v[a] = v[a] + v[b] + x;
            v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
            v[c] += v[d];
            v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
            v[a] = v[a] + v[b] + y;
            v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
            v[c] += v[d];
            v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
        }
    }
}
