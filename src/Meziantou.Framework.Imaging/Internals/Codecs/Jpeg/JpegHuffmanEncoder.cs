namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoder side of a JPEG Huffman table: the code and code length of each symbol, derived from the <c>BITS</c> and
/// <c>HUFFVAL</c> lists of a DHT definition by the canonical procedure of ITU-T T.81 Annex C (figures C.1 to C.3: code
/// lengths in symbol order, consecutive codes within a length, a left shift when the length increases).
/// </summary>
internal sealed class JpegHuffmanEncoder
{
    private readonly ushort[] _codes = new ushort[256];
    private readonly byte[] _lengths = new byte[256];

    /// <summary>Builds the table.</summary>
    /// <param name="bits">The number of codes of each length 1-16.</param>
    /// <param name="values">The symbols, in code order.</param>
    /// <exception cref="ArgumentException">The definition is inconsistent (wrong count, duplicate symbol, or oversubscribed lengths).</exception>
    public JpegHuffmanEncoder(ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values)
    {
        if (bits.Length != 16)
            throw new ArgumentException("A Huffman table defines code counts for the lengths 1 to 16.", nameof(bits));

        var total = 0;
        foreach (var count in bits)
        {
            total += count;
        }

        if (total != values.Length || total > 256)
            throw new ArgumentException("The number of symbols does not match the code counts.", nameof(values));

        Bits = bits.ToArray();
        Values = values.ToArray();
        var code = 0;
        var k = 0;
        for (var length = 1; length <= 16; length++)
        {
            for (var i = 0; i < bits[length - 1]; i++)
            {
                var symbol = values[k++];
                if (_lengths[symbol] != 0)
                    throw new ArgumentException("A symbol appears twice in the Huffman table.", nameof(values));

                _codes[symbol] = (ushort)code;
                _lengths[symbol] = (byte)length;
                code++;
            }

            // A code of all ones is reserved (T.81 C.2): every code of this length must stay below 2^length - 1
            if (code > (1 << length) - (length == 16 ? 1 : 0))
                throw new ArgumentException("The Huffman code lengths are oversubscribed.", nameof(bits));

            code <<= 1;
        }
    }

    /// <summary>Gets the <c>BITS</c> list (written in the DHT segment).</summary>
    public ReadOnlyMemory<byte> Bits { get; }

    /// <summary>Gets the <c>HUFFVAL</c> list (written in the DHT segment).</summary>
    public ReadOnlyMemory<byte> Values { get; }

    /// <summary>Gets the code of a symbol (right-aligned).</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>The code.</returns>
    public uint GetCode(int symbol) => _codes[symbol];

    /// <summary>Gets the code length of a symbol, or 0 when the table does not define it.</summary>
    /// <param name="symbol">The symbol.</param>
    /// <returns>The length in bits.</returns>
    public int GetLength(int symbol) => _lengths[symbol];
}
