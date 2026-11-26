namespace Meziantou.Framework.WebTransport.Http3;

// Huffman encoding of the field names and values (RFC 7541, section 5.2)
internal static partial class Huffman
{
    private const int EndOfString = 256;

    // A binary tree: node n has its children at [2n] (bit 0) and [2n+1] (bit 1). A child is the index of another node, or
    // ~symbol for a leaf. 0 means no child (node 0 is the root, which is never a child).
    private static readonly int[] DecodingTree = BuildDecodingTree();

    private static int[] BuildDecodingTree()
    {
        var tree = new int[2 * 512];
        var nodeCount = 1;
        for (var symbol = 0; symbol <= EndOfString; symbol++)
        {
            var code = Codes[symbol];
            var length = BitLengths[symbol];
            var node = 0;
            for (var i = 0; i < length; i++)
            {
                var bit = (int)((code >> (31 - i)) & 1);
                var slot = (2 * node) + bit;
                if (i == length - 1)
                {
                    tree[slot] = ~symbol;
                }
                else
                {
                    if (tree[slot] == 0)
                    {
                        tree[slot] = nodeCount++;
                    }

                    node = tree[slot];
                }
            }
        }

        return tree;
    }

    public static int GetEncodedLength(ReadOnlySpan<byte> value)
    {
        long bits = 0;
        foreach (var b in value)
        {
            bits += BitLengths[b];
        }

        return (int)((bits + 7) / 8);
    }

    public static void Encode(ReadOnlySpan<byte> value, List<byte> destination)
    {
        ulong accumulator = 0;
        var bitCount = 0;
        foreach (var b in value)
        {
            var length = BitLengths[b];
            accumulator = (accumulator << length) | (Codes[b] >> (32 - length));
            bitCount += length;
            while (bitCount >= 8)
            {
                bitCount -= 8;
                destination.Add((byte)(accumulator >> bitCount));
            }
        }

        if (bitCount > 0)
        {
            // The padding is the most significant bits of EOS: all ones
            destination.Add((byte)((accumulator << (8 - bitCount)) | (0xFFu >> bitCount)));
        }
    }

    public static byte[] Decode(ReadOnlySpan<byte> encoded)
    {
        var result = new List<byte>(encoded.Length * 8 / 5);
        var node = 0;
        var depth = 0;
        var paddingIsOnes = true;
        foreach (var b in encoded)
        {
            for (var i = 7; i >= 0; i--)
            {
                var bit = (b >> i) & 1;
                var child = DecodingTree[(2 * node) + bit];
                if (child == 0)
                    throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Invalid Huffman code.");

                if (child < 0)
                {
                    var symbol = ~child;
                    if (symbol == EndOfString)
                        throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "The Huffman-encoded string contains EOS.");

                    result.Add((byte)symbol);
                    node = 0;
                    depth = 0;
                    paddingIsOnes = true;
                }
                else
                {
                    node = child;
                    depth++;
                    paddingIsOnes &= bit == 1;
                }
            }
        }

        // The padding is a prefix of EOS (all ones) shorter than 8 bits
        if (depth > 7 || !paddingIsOnes)
            throw new Http3ProtocolException(Http3ErrorCode.QPackDecompressionFailed, "Invalid Huffman padding.");

        return [.. result];
    }
}
