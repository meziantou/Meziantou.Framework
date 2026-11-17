using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>
/// SHA-1, with the variant of the key derivation of RAR 2.9 (<see cref="AppendRar29"/>): the 64-byte blocks that are
/// hashed directly from the data (not through the internal buffer) are overwritten with the last 16 words of their
/// message schedule, in little-endian order, which changes the data of the next rounds.
/// </summary>
internal sealed class Rar29Sha1
{
    private readonly uint[] _state = [0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0];
    private readonly byte[] _buffer = new byte[64];
    private readonly uint[] _schedule = new uint[16];
    private ulong _length;

    public Rar29Sha1 Clone()
    {
        var clone = new Rar29Sha1();
        _state.CopyTo(clone._state, 0);
        _buffer.CopyTo(clone._buffer, 0);
        clone._length = _length;
        return clone;
    }

    /// <summary>Appends data (standard SHA-1).</summary>
    public void Append(ReadOnlySpan<byte> data) => Append(data, null);

    /// <summary>Appends data, overwriting the blocks hashed in place with their message schedule (the RAR 2.9 variant).</summary>
    public void AppendRar29(byte[] data) => Append(data, data);

    private void Append(ReadOnlySpan<byte> data, byte[]? writableData)
    {
        var used = (int)(_length & 63);
        _length += (ulong)data.Length;
        var index = 0;
        if (used + data.Length > 63)
        {
            index = 64 - used;
            data[..index].CopyTo(_buffer.AsSpan(used));
            Transform(_buffer);
            for (; index + 63 < data.Length; index += 64)
            {
                Transform(data.Slice(index, 64));
                if (writableData is not null)
                {
                    for (var k = 0; k < 16; k++)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(writableData.AsSpan(index + (k * 4)), _schedule[k]);
                    }
                }
            }

            used = 0;
        }

        data[index..].CopyTo(_buffer.AsSpan(used));
    }

    public void GetHash(Span<byte> destination)
    {
        var bitLength = _length * 8;
        Span<byte> padding = stackalloc byte[72];
        padding.Clear();
        padding[0] = 0x80;
        var used = (int)(_length & 63);
        var padLength = used < 56 ? 56 - used : 120 - used;
        Append(padding[..padLength]);
        BinaryPrimitives.WriteUInt64BigEndian(padding, bitLength);
        Append(padding[..8]);
        for (var i = 0; i < 5; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination[(i * 4)..], _state[i]);
        }
    }

    private void Transform(ReadOnlySpan<byte> block)
    {
        var w = _schedule;
        for (var i = 0; i < 16; i++)
        {
            w[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);
        }

        uint a = _state[0], b = _state[1], c = _state[2], d = _state[3], e = _state[4];
        for (var i = 0; i < 80; i++)
        {
            uint word;
            if (i < 16)
            {
                word = w[i];
            }
            else
            {
                word = BitOperations.RotateLeft(w[(i + 13) & 15] ^ w[(i + 8) & 15] ^ w[(i + 2) & 15] ^ w[i & 15], 1);
                w[i & 15] = word;
            }

            uint f, k;
            if (i < 20)
            {
                f = (b & c) | (~b & d);
                k = 0x5A827999;
            }
            else if (i < 40)
            {
                f = b ^ c ^ d;
                k = 0x6ED9EBA1;
            }
            else if (i < 60)
            {
                f = (b & c) | (b & d) | (c & d);
                k = 0x8F1BBCDC;
            }
            else
            {
                f = b ^ c ^ d;
                k = 0xCA62C1D6;
            }

            var temp = BitOperations.RotateLeft(a, 5) + f + e + k + word;
            e = d;
            d = c;
            c = BitOperations.RotateLeft(b, 30);
            b = a;
            a = temp;
        }

        _state[0] += a;
        _state[1] += b;
        _state[2] += c;
        _state[3] += d;
        _state[4] += e;
    }
}
