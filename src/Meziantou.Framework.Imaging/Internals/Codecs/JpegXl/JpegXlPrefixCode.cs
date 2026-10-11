namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the prefix codes of the entropy coder (ISO/IEC 18181-1, section D.2, which is the prefix code of RFC 7932,
/// section 3): a simple code of one to four symbols, or code lengths that are themselves coded with a prefix code.
/// </summary>
internal static class JpegXlPrefixCode
{
    /// <summary>The largest length of a code.</summary>
    public const int MaxLength = 15;

    private const int CodeLengthCodes = 18;
    private const int RepeatPreviousCode = 16;

    private static ReadOnlySpan<byte> CodeLengthCodeOrder => [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    /// Reads the code of an alphabet and builds its lookup table: indexed by the next <paramref name="maxLength"/> bits of
    /// the stream, an entry holds the symbol (shifted left by 4) and the length of its code.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="alphabetSize">The size of the alphabet (2 to 32768).</param>
    /// <param name="scope">The scope the table is rented from.</param>
    /// <param name="maxLength">Receives the length of the longest code: the number of bits that index the table.</param>
    /// <returns>The table.</returns>
    public static Av1PooledArray<int> Read(JpegXlBitReader reader, int alphabetSize, AllocationScope scope, out int maxLength)
    {
        var lengths = scope.Rent(alphabetSize, AllocationKind.DecoderState, clear: true);
        try
        {
            var span = lengths.RawBuffer.AsSpan(0, alphabetSize);
            var skip = (int)reader.ReadBits(2);
            if (skip == 1)
            {
                ReadSimpleCode(reader, span);
            }
            else
            {
                ReadCodeLengths(reader, skip, span);
            }

            return BuildTable(span, scope, out maxLength);
        }
        finally
        {
            lengths.Dispose();
        }
    }

    /// <summary>Reads the symbol that the next bits of the stream code.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint ReadSymbol(JpegXlBitReader reader, ReadOnlySpan<int> table, int maxLength)
    {
        var entry = table[(int)reader.PeekBits(maxLength)];
        reader.Consume(entry & 15);
        return (uint)entry >> 4;
    }

    private static void ReadSimpleCode(JpegXlBitReader reader, Span<byte> lengths)
    {
        var symbolBits = JpegXlHybridUintConfig.CeilingLog2(lengths.Length);
        var count = (int)reader.ReadBits(2) + 1;
        Span<int> symbols = stackalloc int[4];
        for (var i = 0; i < count; i++)
        {
            symbols[i] = (int)reader.ReadBits(symbolBits);
            if (symbols[i] >= lengths.Length)
                throw JpegXlFormat.Invalid("a prefix code uses a symbol outside its alphabet.");

            for (var j = 0; j < i; j++)
            {
                if (symbols[j] == symbols[i])
                    throw JpegXlFormat.Invalid("a prefix code uses a symbol twice.");
            }
        }

        switch (count)
        {
            case 1:
                // A single symbol has a code of no bit: it is marked, and the table is its only entry
                lengths[symbols[0]] = byte.MaxValue;
                break;
            case 2:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 1;
                break;
            case 3:
                lengths[symbols[0]] = 1;
                lengths[symbols[1]] = 2;
                lengths[symbols[2]] = 2;
                break;
            default:
                if (reader.ReadBool())
                {
                    lengths[symbols[0]] = 1;
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = 3;
                    lengths[symbols[3]] = 3;
                }
                else
                {
                    lengths[symbols[0]] = 2;
                    lengths[symbols[1]] = 2;
                    lengths[symbols[2]] = 2;
                    lengths[symbols[3]] = 2;
                }

                break;
        }
    }

    private static void ReadCodeLengths(JpegXlBitReader reader, int skip, Span<byte> lengths)
    {
        // The lengths of the code that codes the lengths, with a fixed code: 00 -> 0, 10 -> 3, 01 -> 4, 110 -> 2, 0111 -> 1, 1111 -> 5 (first bit read on the right)
        Span<byte> codeLengthLengths = stackalloc byte[CodeLengthCodes];
        codeLengthLengths.Clear();
        var space = 32;
        var codeCount = 0;
        for (var i = skip; i < CodeLengthCodes && space > 0; i++)
        {
            var bits = reader.PeekBits(4);
            int value;
            int length;
            switch (bits & 3)
            {
                case 0:
                    (value, length) = (0, 2);
                    break;
                case 1:
                    (value, length) = (4, 2);
                    break;
                case 2:
                    (value, length) = (3, 2);
                    break;
                default:
                    (value, length) = (bits & 4) == 0 ? (2, 3) : (bits & 8) == 0 ? (1, 4) : (5, 4);
                    break;
            }

            reader.Consume(length);
            codeLengthLengths[CodeLengthCodeOrder[i]] = (byte)value;
            if (value != 0)
            {
                space -= 32 >> value;
                codeCount++;
            }
        }

        if (codeCount != 1 && space != 0)
            throw JpegXlFormat.Invalid("the lengths of a prefix code are not coded with a complete code.");

        Span<int> codeLengthTable = stackalloc int[32];
        BuildTable(codeLengthLengths, codeLengthTable, 5);

        var symbol = 0;
        var previousLength = 8;
        var repeat = 0;
        var repeatLength = 0;
        var remaining = 32768;
        while (symbol < lengths.Length && remaining > 0)
        {
            var entry = codeLengthTable[(int)reader.PeekBits(5)];
            reader.Consume(entry & 15);
            var code = entry >> 4;
            if (code < RepeatPreviousCode)
            {
                repeat = 0;
                lengths[symbol++] = (byte)code;
                if (code != 0)
                {
                    previousLength = code;
                    remaining -= 32768 >> code;
                }
            }
            else
            {
                // 16 repeats the previous length 3 to 6 times, 17 repeats a length of zero 3 to 10 times; consecutive repeats multiply
                var extraBits = code - 14;
                var newLength = code == RepeatPreviousCode ? previousLength : 0;
                if (repeatLength != newLength)
                {
                    repeat = 0;
                    repeatLength = newLength;
                }

                var oldRepeat = repeat;
                if (repeat > 0)
                {
                    repeat = (repeat - 2) << extraBits;
                }

                repeat += (int)reader.ReadBits(extraBits) + 3;
                var delta = repeat - oldRepeat;
                if (delta > lengths.Length - symbol)
                    throw JpegXlFormat.Invalid("the lengths of a prefix code exceed its alphabet.");

                lengths.Slice(symbol, delta).Fill((byte)repeatLength);
                symbol += delta;
                if (repeatLength != 0)
                {
                    remaining -= delta << (15 - repeatLength);
                }
            }

            if (reader.IsOverrun)
                throw JpegXlFormat.Invalid("a prefix code is truncated.");
        }

        if (remaining != 0)
            throw JpegXlFormat.Invalid("a prefix code is not complete.");
    }

    private static Av1PooledArray<int> BuildTable(ReadOnlySpan<byte> lengths, AllocationScope scope, out int maxLength)
    {
        maxLength = 0;
        foreach (var length in lengths)
        {
            if (length == byte.MaxValue)
            {
                // The single symbol of a simple code
                maxLength = 0;
                break;
            }

            maxLength = Math.Max(maxLength, length);
        }

        var table = Av1PooledArray<int>.Rent(scope, 1L << maxLength, clear: false);
        try
        {
            BuildTable(lengths, table.Span, maxLength);
            return table;
        }
        catch
        {
            table.Dispose();
            throw;
        }
    }

    /// <summary>Fills the lookup table of the canonical code of the given lengths: shorter codes first, then increasing symbols; the bits of a code are read first bit first.</summary>
    private static void BuildTable(ReadOnlySpan<byte> lengths, Span<int> table, int tableBits)
    {
        Span<int> counts = stackalloc int[MaxLength + 1];
        counts.Clear();
        var single = -1;
        var used = 0;
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length == byte.MaxValue)
            {
                table.Fill(symbol << 4);
                return;
            }

            if (length != 0)
            {
                counts[length]++;
                single = symbol;
                used++;
            }
        }

        if (used == 1)
        {
            // A code of one symbol takes no bit, whatever its length
            table.Fill(single << 4);
            return;
        }

        Span<int> next = stackalloc int[MaxLength + 1];
        var code = 0;
        for (var length = 1; length <= MaxLength; length++)
        {
            code = (code + counts[length - 1]) << 1;
            next[length] = code;
        }

        table.Clear();
        for (var symbol = 0; symbol < lengths.Length; symbol++)
        {
            var length = lengths[symbol];
            if (length == 0)
                continue;

            var canonical = next[length]++;
            if (length > tableBits || canonical >= 1 << length)
                throw JpegXlFormat.Invalid("a prefix code is over-subscribed.");

            // Reverse the bits of the code: the stream gives its first bit first, which is the lowest bit of the index
            var reversed = 0;
            for (var bit = 0; bit < length; bit++)
            {
                reversed |= ((canonical >> bit) & 1) << (length - 1 - bit);
            }

            var entry = (symbol << 4) | length;
            for (var index = reversed; index < table.Length; index += 1 << length)
            {
                table[index] = entry;
            }
        }
    }
}
