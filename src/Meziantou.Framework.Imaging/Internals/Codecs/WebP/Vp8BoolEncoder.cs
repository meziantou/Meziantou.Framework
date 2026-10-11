namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The boolean entropy encoder of VP8 (RFC 6386 section 7.3), the inverse of <see cref="Vp8BoolDecoder"/>: it narrows the
/// coding interval with each boolean and its 8-bit probability, writes bytes as they become final (propagating carries into
/// bytes already written), and flushes four bytes at the end.
/// </summary>
internal sealed class Vp8BoolEncoder : IDisposable
{
    private readonly WebPPayloadWriter _output;
    private uint _range = 255;
    private uint _bottom;
    private int _bitCount = 24;

    public Vp8BoolEncoder(AllocationScope scope)
    {
        _output = new WebPPayloadWriter(scope);
    }

    /// <summary>Gets the encoded bytes (after <see cref="Flush"/>).</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _output.WrittenMemory;

    /// <summary>Gets the number of bytes written so far.</summary>
    public int Length => _output.Length;

    /// <summary>Encodes a boolean whose probability of being <see langword="false"/> is <paramref name="probability"/>/256.</summary>
    public void WriteBool(int probability, bool value)
    {
        var split = 1 + (((_range - 1) * (uint)probability) >> 8);
        if (value)
        {
            _bottom += split;
            _range -= split;
        }
        else
        {
            _range = split;
        }

        while (_range < 128)
        {
            _range <<= 1;
            if ((_bottom & (1u << 31)) != 0)
            {
                _output.PropagateCarry(_output.Length - 1);
            }

            _bottom <<= 1;
            if (--_bitCount == 0)
            {
                _output.Write((byte)(_bottom >> 24));
                _bottom &= (1u << 24) - 1;
                _bitCount = 8;
            }
        }
    }

    /// <summary>Encodes a boolean with probability 1/2.</summary>
    public void WriteBit(bool value) => WriteBool(128, value);

    /// <summary>Encodes an unsigned literal of <paramref name="bits"/> bits, most significant first (<c>L(n)</c>).</summary>
    public void WriteLiteral(int value, int bits)
    {
        while (bits-- > 0)
        {
            WriteBit(((value >> bits) & 1) != 0);
        }
    }

    /// <summary>Encodes an optional signed value: the presence flag, then the magnitude and the sign when nonzero.</summary>
    public void WriteOptionalSigned(int value, int bits)
    {
        WriteBit(value != 0);
        if (value != 0)
        {
            WriteLiteral(Math.Abs(value), bits);
            WriteBit(value < 0);
        }
    }

    /// <summary>Encodes a tree-coded value (the inverse of <see cref="Vp8BoolDecoder.ReadTree"/>).</summary>
    public void WriteTree(ReadOnlySpan<sbyte> tree, ReadOnlySpan<byte> probabilities, int value)
    {
        Span<int> path = stackalloc int[16];
        var length = FindPath(tree, 0, -value, path, 0);
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a leaf of the tree.");

        var node = 0;
        for (var i = 0; i < length; i++)
        {
            var bit = path[i];
            WriteBool(probabilities[node >> 1], bit != 0);
            node = tree[node + bit];
        }
    }

    /// <summary>Flushes the remaining bits (four bytes).</summary>
    public void Flush()
    {
        var count = _bitCount;
        var value = _bottom;
        if ((value & (1u << (32 - count))) != 0)
        {
            _output.PropagateCarry(_output.Length - 1);
        }

        value <<= count & 7;
        count >>= 3;
        while (--count >= 0)
        {
            value <<= 8;
        }

        for (var i = 0; i < 4; i++)
        {
            _output.Write((byte)(value >> 24));
            value <<= 8;
        }
    }

    public void Dispose() => _output.Dispose();

    private static int FindPath(ReadOnlySpan<sbyte> tree, int node, int leaf, Span<int> path, int depth)
    {
        for (var bit = 0; bit < 2; bit++)
        {
            var next = tree[node + bit];
            path[depth] = bit;
            if (next <= 0)
            {
                if (next == leaf)
                    return depth + 1;
            }
            else
            {
                var length = FindPath(tree, next, leaf, path, depth + 1);
                if (length >= 0)
                    return length;
            }
        }

        return -1;
    }
}
