using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Huffman coder of the PIZ compression (compression notes, section 5.4): 16-bit symbols and one run symbol (the
/// largest coded symbol, followed by an 8-bit repeat count), a bit-packed table of code lengths, canonical codes in which
/// longer codes are numerically smaller, and MSB-first bit packing.
/// </summary>
/// <remarks>The tables of one coder are rented once from the scope of its owner and reused for every chunk.</remarks>
internal sealed class ExrHuffman : IDisposable
{
    /// <summary>The number of symbols: the 65,536 values and the run symbol 65,536.</summary>
    public const int SymbolCount = 65537;

    /// <summary>The longest code a valid stream has (58), and the longest this encoder writes (57: see section 5.4.3).</summary>
    public const int MaxCodeLength = 58;

    private const int EncoderMaxCodeLength = 57;
    private const int HeaderLength = 20;
    private const int LookupBits = 12;

    private readonly PooledBuffer _lengths;
    private readonly PooledBuffer _symbols;
    private readonly PooledBuffer _lookup;
    private readonly PooledBuffer? _codes;
    private readonly PooledBuffer? _frequencies;
    private readonly PooledBuffer? _nodes;
    private readonly long[] _start = new long[MaxCodeLength + 2];
    private readonly int[] _count = new int[MaxCodeLength + 2];
    private readonly int[] _firstSymbol = new int[MaxCodeLength + 2];

    public ExrHuffman(AllocationScope scope, bool encoder)
    {
        _lengths = scope.Rent(SymbolCount, AllocationKind.DecoderState);
        try
        {
            _symbols = scope.Rent(SymbolCount * sizeof(int), AllocationKind.DecoderState, clear: false);
            _lookup = scope.Rent((1 << LookupBits) * sizeof(int), AllocationKind.DecoderState, clear: false);
            if (encoder)
            {
                _codes = scope.Rent(SymbolCount * sizeof(ulong), AllocationKind.DecoderState, clear: false);
                _frequencies = scope.Rent(SymbolCount * sizeof(long), AllocationKind.DecoderState);

                // Leaves and internal nodes of the tree: a weight and a parent each
                _nodes = scope.Rent(2 * SymbolCount * (sizeof(long) + sizeof(int)), AllocationKind.DecoderState, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Span<byte> Lengths => _lengths.Span;

    private Span<int> Symbols => unsafe(MemoryMarshal.Cast<byte, int>(_symbols.Span));

    private Span<int> Lookup => unsafe(MemoryMarshal.Cast<byte, int>(_lookup.Span));

    /// <summary>Decodes a Huffman stream (section 5.4.1) into exactly <paramref name="destination"/>.Length symbols.</summary>
    /// <param name="stream">The Huffman stream, as delimited by the length field of the PIZ chunk.</param>
    /// <param name="destination">Receives the 16-bit values.</param>
    /// <exception cref="InvalidImageContentException">The stream is malformed.</exception>
    public void Decode(ReadOnlySpan<byte> stream, Span<ushort> destination)
    {
        if (stream.Length < HeaderLength)
        {
            if (destination.IsEmpty)
                return;

            throw ExrFormat.Invalid("the Huffman data of a PIZ chunk are truncated.");
        }

        var min = BinaryPrimitives.ReadUInt32LittleEndian(stream);
        var max = BinaryPrimitives.ReadUInt32LittleEndian(stream[4..]);
        var bitCount = BinaryPrimitives.ReadUInt32LittleEndian(stream[12..]);
        if (min >= SymbolCount || max >= SymbolCount || min > max)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the Huffman symbols of a PIZ chunk range from {min} to {max}."));

        var lengths = Lengths;
        lengths.Clear();
        var tableEnd = ReadTable(stream[HeaderLength..], (int)min, (int)max, lengths) + HeaderLength;
        var dataLength = (bitCount + 7L) / 8;
        if (dataLength > stream.Length - tableEnd)
            throw ExrFormat.Invalid("the Huffman data of a PIZ chunk extend past the end of the chunk.");

        BuildCanonicalCodes(lengths, (int)min, (int)max, codes: default);
        BuildLookup();
        DecodeSymbols(stream.Slice(tableEnd, (int)dataLength), (long)bitCount, (int)max, destination);
    }

    /// <summary>Encodes 16-bit values as a Huffman stream.</summary>
    /// <param name="source">The values.</param>
    /// <param name="destination">Receives the stream.</param>
    /// <returns>The length of the stream, or -1 when it does not fit <paramref name="destination"/>.</returns>
    public int Encode(ReadOnlySpan<ushort> source, Span<byte> destination)
    {
        if (source.IsEmpty)
            return 0;

        var frequencies = unsafe(MemoryMarshal.Cast<byte, long>(_frequencies!.Span));
        frequencies.Clear();
        var min = int.MaxValue;
        var maxValue = 0;
        foreach (var value in source)
        {
            frequencies[value]++;
            min = Math.Min(min, value);
            maxValue = Math.Max(maxValue, value);
        }

        // The run symbol is the largest coded symbol, always present (section 5.4.1)
        var runSymbol = maxValue + 1;
        frequencies[runSymbol] = 1;
        var lengths = Lengths;
        lengths.Clear();
        BuildLengths(frequencies, min, runSymbol, lengths);
        var codes = unsafe(MemoryMarshal.Cast<byte, ulong>(_codes!.Span));
        BuildCanonicalCodes(lengths, min, runSymbol, codes);

        if (destination.Length < HeaderLength)
            return -1;

        var writer = new BitWriter(destination, HeaderLength);
        if (!WriteTable(ref writer, lengths, min, runSymbol))
            return -1;

        var tableLength = writer.ByteLength - HeaderLength;
        writer = new BitWriter(destination, writer.ByteLength);
        var dataStart = writer.ByteStart;
        if (!WriteSymbols(ref writer, source, lengths, codes, runSymbol))
            return -1;

        var bitCount = writer.BitLength;
        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)min);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)runSymbol);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)tableLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], (uint)bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], 0);
        return dataStart + (int)((bitCount + 7) / 8);
    }

    public void Dispose()
    {
        _lengths?.Dispose();
        _symbols?.Dispose();
        _lookup?.Dispose();
        _codes?.Dispose();
        _frequencies?.Dispose();
        _nodes?.Dispose();
    }

    /// <summary>Reads the packed code lengths (section 5.4.2).</summary>
    /// <returns>The number of bytes of the table, padding included.</returns>
    private static int ReadTable(ReadOnlySpan<byte> table, int min, int max, Span<byte> lengths)
    {
        var reader = new BitReader(table, (long)table.Length * 8);
        var symbol = min;
        while (symbol <= max)
        {
            if (!reader.TryRead(6, out var value))
                throw ExrFormat.Invalid("the Huffman code lengths of a PIZ chunk are truncated.");

            int zeros;
            if (value <= MaxCodeLength)
            {
                lengths[symbol++] = (byte)value;
                continue;
            }

            if (value < 63)
            {
                zeros = (int)value - 57;
            }
            else
            {
                if (!reader.TryRead(8, out var run))
                    throw ExrFormat.Invalid("the Huffman code lengths of a PIZ chunk are truncated.");

                zeros = (int)run + 6;
            }

            if (zeros > max - symbol + 1)
                throw ExrFormat.Invalid("a run of unused symbols of the Huffman code lengths of a PIZ chunk extends past the last symbol.");

            symbol += zeros;
        }

        return (int)((reader.Position + 7) / 8);
    }

    /// <summary>
    /// Assigns the canonical codes (section 5.4.3): the starting code of each length from the longest down, then the symbols
    /// in increasing order. Also fills the symbols sorted by length then value, for the decoder.
    /// </summary>
    private void BuildCanonicalCodes(ReadOnlySpan<byte> lengths, int min, int max, Span<ulong> codes)
    {
        Array.Clear(_count);
        for (var symbol = min; symbol <= max; symbol++)
        {
            _count[lengths[symbol]]++;
        }

        _count[0] = 0;
        long code = 0;
        for (var length = MaxCodeLength; length >= 1; length--)
        {
            _start[length] = code;
            code = (code + _count[length]) >> 1;
        }

        // Complete (a full binary tree): the code space of every length is exactly filled
        UInt128 space = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            space += (UInt128)_count[length] << (MaxCodeLength - length);
        }

        if (space != (UInt128)1 << MaxCodeLength)
            throw ExrFormat.Invalid("the Huffman code of a PIZ chunk is not a complete prefix code.");

        // Symbols grouped by length, each group in increasing order
        var first = 0;
        for (var length = 1; length <= MaxCodeLength; length++)
        {
            _firstSymbol[length] = first;
            first += _count[length];
        }

        Span<int> next = stackalloc int[MaxCodeLength + 2];
        Span<long> nextCode = stackalloc long[MaxCodeLength + 2];
        _firstSymbol.CopyTo(next);
        _start.CopyTo(nextCode);
        var symbols = Symbols;
        for (var symbol = min; symbol <= max; symbol++)
        {
            var length = lengths[symbol];
            if (length == 0)
                continue;

            symbols[next[length]++] = symbol;
            if (!codes.IsEmpty)
            {
                codes[symbol] = (ulong)nextCode[length]++;
            }
        }
    }

    /// <summary>Fills the table of the codes of at most <see cref="LookupBits"/> bits: (symbol + 1) &lt;&lt; 8 | length, or 0.</summary>
    private void BuildLookup()
    {
        var lookup = Lookup;
        lookup.Clear();
        var symbols = Symbols;
        for (var length = 1; length <= LookupBits; length++)
        {
            for (var k = 0; k < _count[length]; k++)
            {
                var code = _start[length] + k;
                var entry = ((symbols[_firstSymbol[length] + k] + 1) << 8) | length;
                var shift = LookupBits - length;
                lookup.Slice((int)(code << shift), 1 << shift).Fill(entry);
            }
        }
    }

    private void DecodeSymbols(ReadOnlySpan<byte> data, long bitCount, int runSymbol, Span<ushort> destination)
    {
        var reader = new BitReader(data, bitCount);
        var lookup = Lookup;
        var symbols = Symbols;
        var output = 0;
        while (output < destination.Length)
        {
            var peek = reader.Peek(LookupBits);
            var entry = lookup[(int)peek];
            int symbol;
            int length;
            if (entry != 0)
            {
                length = entry & 0xFF;
                symbol = (entry >> 8) - 1;
            }
            else
            {
                // A longer code: its first bits are the peeked ones
                long code = (long)peek;
                length = LookupBits;
                symbol = -1;
                while (length < MaxCodeLength)
                {
                    length++;
                    code = (code << 1) | (long)reader.PeekBit(length - 1);
                    var index = code - _start[length];
                    if (index >= 0 && index < _count[length])
                    {
                        symbol = symbols[_firstSymbol[length] + (int)index];
                        break;
                    }
                }

                if (symbol < 0)
                    throw ExrFormat.Invalid("the Huffman data of a PIZ chunk have a code that is not defined.");
            }

            if (!reader.TrySkip(length))
                throw ExrFormat.Invalid("the Huffman data of a PIZ chunk end in the middle of a code.");

            if (symbol == runSymbol)
            {
                if (output == 0)
                    throw ExrFormat.Invalid("the Huffman data of a PIZ chunk start with a repetition.");

                if (!reader.TryRead(8, out var repeat))
                    throw ExrFormat.Invalid("the Huffman data of a PIZ chunk end in the middle of a repetition.");

                if ((int)repeat > destination.Length - output)
                    throw ExrFormat.Invalid("a repetition of the Huffman data of a PIZ chunk extends past the end of the chunk.");

                destination.Slice(output, (int)repeat).Fill(destination[output - 1]);
                output += (int)repeat;
            }
            else
            {
                destination[output++] = (ushort)symbol;
            }
        }

        if (reader.Position != bitCount)
            throw ExrFormat.Invalid("the Huffman data of a PIZ chunk have codes after the last value.");
    }

    /// <summary>
    /// Computes Huffman code lengths with the two-queue construction over the symbols sorted by frequency (ties by symbol).
    /// Frequencies below 2^31 never give a code longer than 45 bits, so no limit has to be enforced.
    /// </summary>
    private void BuildLengths(ReadOnlySpan<long> frequencies, int min, int max, Span<byte> lengths)
    {
        var nodeBytes = _nodes!.Span;
        var weights = unsafe(MemoryMarshal.Cast<byte, long>(nodeBytes[..(2 * SymbolCount * sizeof(long))]));
        var parents = unsafe(MemoryMarshal.Cast<byte, int>(nodeBytes[(2 * SymbolCount * sizeof(long))..]));
        var symbols = Symbols;

        // Leaves sorted by (frequency, symbol): the frequency in the high bits of the key
        var leafCount = 0;
        for (var symbol = min; symbol <= max; symbol++)
        {
            if (frequencies[symbol] != 0)
            {
                weights[leafCount] = (frequencies[symbol] << 17) | (long)symbol;
                symbols[leafCount] = symbol;
                leafCount++;
            }
        }

        weights[..leafCount].Sort(symbols[..leafCount]);
        for (var i = 0; i < leafCount; i++)
        {
            weights[i] >>= 17;
        }

        // Internal nodes are created in order of non-decreasing weight: two queues give the two smallest nodes
        var leaf = 0;
        var node = leafCount;
        var end = leafCount;
        while (end < (2 * leafCount) - 1)
        {
            var first = TakeSmallest(weights, ref leaf, leafCount, ref node, end);
            var second = TakeSmallest(weights, ref leaf, leafCount, ref node, end);
            weights[end] = weights[first] + weights[second];
            parents[first] = end;
            parents[second] = end;
            end++;
        }

        // Depths from the root (the last node) down: a parent is always created after its children
        var depths = unsafe(MemoryMarshal.Cast<long, int>(weights));
        depths[end - 1] = 0;
        for (var i = end - 2; i >= 0; i--)
        {
            depths[i] = depths[parents[i]] + 1;
        }

        for (var i = 0; i < leafCount; i++)
        {
            var depth = depths[i];
            if (depth > EncoderMaxCodeLength)
                throw new InvalidOperationException("A Huffman code of the PIZ encoder is longer than 57 bits.");

            lengths[symbols[i]] = (byte)depth;
        }
    }

    private static int TakeSmallest(Span<long> weights, ref int leaf, int leafCount, ref int node, int end)
    {
        if (leaf < leafCount && (node >= end || weights[leaf] <= weights[node]))
            return leaf++;

        return node++;
    }

    /// <summary>Writes the packed code lengths (section 5.4.2), with the zero runs of the reference encoder.</summary>
    private static bool WriteTable(ref BitWriter writer, ReadOnlySpan<byte> lengths, int min, int max)
    {
        var symbol = min;
        while (symbol <= max)
        {
            var length = lengths[symbol];
            if (length != 0)
            {
                if (!writer.TryWrite(length, 6))
                    return false;

                symbol++;
                continue;
            }

            var zeros = 1;
            while (symbol + zeros <= max && lengths[symbol + zeros] == 0)
            {
                zeros++;
            }

            symbol += zeros;
            while (zeros > 0)
            {
                bool written;
                if (zeros >= 6)
                {
                    var run = Math.Min(zeros, 261);
                    written = writer.TryWrite(63, 6) && writer.TryWrite((ulong)(run - 6), 8);
                    zeros -= run;
                }
                else if (zeros >= 2)
                {
                    written = writer.TryWrite((ulong)(zeros + 57), 6);
                    zeros = 0;
                }
                else
                {
                    written = writer.TryWrite(0, 6);
                    zeros = 0;
                }

                if (!written)
                    return false;
            }
        }

        return writer.TryAlign();
    }

    /// <summary>Writes the values: runs of equal values as a value, the run symbol and a repeat count when that is shorter.</summary>
    private static bool WriteSymbols(ref BitWriter writer, ReadOnlySpan<ushort> source, ReadOnlySpan<byte> lengths, ReadOnlySpan<ulong> codes, int runSymbol)
    {
        var runLength = lengths[runSymbol];
        var runCode = codes[runSymbol];
        var i = 0;
        while (i < source.Length)
        {
            var value = source[i];
            var repeats = 0;
            while (repeats < 255 && i + repeats + 1 < source.Length && source[i + repeats + 1] == value)
            {
                repeats++;
            }

            var length = lengths[value];
            var code = codes[value];
            if (repeats > 0 && length + runLength + 8 < length * repeats)
            {
                if (!writer.TryWrite(code, length) || !writer.TryWrite(runCode, runLength) || !writer.TryWrite((ulong)repeats, 8))
                    return false;
            }
            else
            {
                for (var k = 0; k <= repeats; k++)
                {
                    if (!writer.TryWrite(code, length))
                        return false;
                }
            }

            i += repeats + 1;
        }

        return true;
    }

    /// <summary>Reads bits MSB-first, up to a number of meaningful bits.</summary>
    [StructLayout(LayoutKind.Auto)]
    private ref struct BitReader(ReadOnlySpan<byte> data, long bitCount)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private readonly long _bitCount = bitCount;

        public long Position { get; private set; }

        /// <summary>Gets the next bits without consuming them; the bits past the end read as zeros.</summary>
        public readonly ulong Peek(int count)
        {
            ulong value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | PeekBit(i);
            }

            return value;
        }

        /// <summary>Gets the bit at <paramref name="offset"/> bits from the position, or zero past the end.</summary>
        public readonly ulong PeekBit(int offset)
        {
            var bit = Position + offset;
            if (bit >= _bitCount)
                return 0;

            return (ulong)(_data[(int)(bit >> 3)] >> (7 - (int)(bit & 7))) & 1;
        }

        public bool TrySkip(int count)
        {
            if (count > _bitCount - Position)
                return false;

            Position += count;
            return true;
        }

        public bool TryRead(int count, out ulong value)
        {
            if (count > _bitCount - Position)
            {
                value = 0;
                return false;
            }

            value = Peek(count);
            Position += count;
            return true;
        }
    }

    /// <summary>Writes bits MSB-first, the last byte padded with zeros.</summary>
    [StructLayout(LayoutKind.Auto)]
    private ref struct BitWriter(Span<byte> destination, int byteStart)
    {
        private readonly Span<byte> _destination = destination;
        private long _position;

        public readonly int ByteStart => byteStart;

        public readonly long BitLength => _position;

        public readonly int ByteLength => byteStart + (int)((_position + 7) / 8);

        public bool TryWrite(ulong value, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                var byteIndex = byteStart + (int)(_position >> 3);
                if (byteIndex >= _destination.Length)
                    return false;

                var bit = (int)(_position & 7);
                if (bit == 0)
                {
                    _destination[byteIndex] = 0;
                }

                _destination[byteIndex] |= (byte)(((value >> i) & 1) << (7 - bit));
                _position++;
            }

            return true;
        }

        public readonly bool TryAlign() => ByteLength <= _destination.Length;
    }
}
