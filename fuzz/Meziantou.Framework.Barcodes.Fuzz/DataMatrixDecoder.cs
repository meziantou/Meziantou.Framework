using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>
/// An independent Data Matrix (ECC 200) decoder: it reads the codewords of a symbol (ISO/IEC 16022, Annex F), checks its
/// Reed-Solomon codewords and its padding, and decodes its encodation schemes, the way readers end them (a C40, Text or
/// X12 segment returns to ASCII when one codeword is left, an EDIFACT segment when two codewords or less are left).
/// </summary>
internal static class DataMatrixDecoder
{
    /// <summary>The FNC1 function character in the decoded message (the other values are bytes).</summary>
    public const int Fnc1 = 256;

    // Height, width, height and width of a data region, data codewords, error correction codewords, blocks
    private static readonly (int Height, int Width, int RegionHeight, int RegionWidth, int DataCodewords, int ErrorCorrectionCodewords, int Blocks)[] Sizes =
    [
        (10, 10, 8, 8, 3, 5, 1), (12, 12, 10, 10, 5, 7, 1), (14, 14, 12, 12, 8, 10, 1), (16, 16, 14, 14, 12, 12, 1), (18, 18, 16, 16, 18, 14, 1),
        (20, 20, 18, 18, 22, 18, 1), (22, 22, 20, 20, 30, 20, 1), (24, 24, 22, 22, 36, 24, 1), (26, 26, 24, 24, 44, 28, 1), (32, 32, 14, 14, 62, 36, 1),
        (36, 36, 16, 16, 86, 42, 1), (40, 40, 18, 18, 114, 48, 1), (44, 44, 20, 20, 144, 56, 1), (48, 48, 22, 22, 174, 68, 1), (52, 52, 24, 24, 204, 84, 2),
        (64, 64, 14, 14, 280, 112, 2), (72, 72, 16, 16, 368, 144, 4), (80, 80, 18, 18, 456, 192, 4), (88, 88, 20, 20, 576, 224, 4), (96, 96, 22, 22, 696, 272, 4),
        (104, 104, 24, 24, 816, 336, 6), (120, 120, 18, 18, 1050, 408, 6), (132, 132, 20, 20, 1304, 496, 8), (144, 144, 22, 22, 1558, 620, 10),
        (8, 18, 6, 16, 5, 7, 1), (8, 32, 6, 14, 10, 11, 1), (12, 26, 10, 24, 16, 14, 1), (12, 36, 10, 16, 22, 18, 1), (16, 36, 14, 16, 32, 24, 1), (16, 48, 14, 22, 49, 28, 1),
    ];

    private static readonly int[] Exp = new int[255];
    private static readonly int[] Log = new int[256];

#pragma warning disable CA1810 // Initialize reference type static fields inline
    static DataMatrixDecoder()
#pragma warning restore CA1810
    {
        var value = 1;
        for (var i = 0; i < 255; i++)
        {
            Exp[i] = value;
            Log[value] = i;
            value <<= 1;
            if (value >= 256)
            {
                value ^= 0x12D;
            }
        }
    }

    /// <summary>Decodes the symbol.</summary>
    /// <returns>The decoded message, and the ECI assignment number its first codewords declare.</returns>
    public static (List<int> Message, int? Eci) Decode(Barcode2D barcode)
    {
        var index = Array.FindIndex(Sizes, size => size.Height == barcode.Height && size.Width == barcode.Width);
        FuzzAssert.True(index >= 0, $"{barcode.Height} x {barcode.Width} is not a Data Matrix size");
        var size = Sizes[index];

        // The mapping matrix is the data regions without their finder patterns
        var rows = barcode.Height / (size.RegionHeight + 2) * size.RegionHeight;
        var columns = barcode.Width / (size.RegionWidth + 2) * size.RegionWidth;
        var mapping = new bool[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var symbolRow = (row / size.RegionHeight * (size.RegionHeight + 2)) + (row % size.RegionHeight) + 1;
                var symbolColumn = (column / size.RegionWidth * (size.RegionWidth + 2)) + (column % size.RegionWidth) + 1;
                mapping[row, column] = barcode[symbolRow, symbolColumn];
            }
        }

        var codewords = ReadCodewords(mapping, rows, columns, size.DataCodewords + size.ErrorCorrectionCodewords);
        CheckErrorCorrection(codewords, size.DataCodewords, size.ErrorCorrectionCodewords / size.Blocks, size.Blocks);
        return DecodeData(codewords.AsSpan(0, size.DataCodewords));
    }

    private static int[] ReadCodewords(bool[,] mapping, int rows, int columns, int count)
    {
        var codewords = new int[count];
        var read = new bool[rows, columns];
        var position = 0;
        var row = 4;
        var column = 0;

        void Module(int r, int c, int bit)
        {
            if (r < 0)
            {
                r += rows;
                c += 4 - ((rows + 4) % 8);
            }

            if (c < 0)
            {
                c += columns;
                r += 4 - ((columns + 4) % 8);
            }

            FuzzAssert.True(!read[r, c], $"The module ({c}, {r}) of the mapping matrix holds two bits");
            read[r, c] = true;
            if (mapping[r, c])
            {
                codewords[position] |= 1 << (8 - bit);
            }
        }

        void Shape(ReadOnlySpan<(int Row, int Column)> modules)
        {
            FuzzAssert.True(position < count, "The symbol has more codewords than its size");
            for (var i = 0; i < 8; i++)
            {
                Module(modules[i].Row, modules[i].Column, i + 1);
            }

            position++;
        }

        do
        {
            if (row == rows && column == 0)
                Shape([(rows - 1, 0), (rows - 1, 1), (rows - 1, 2), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1)]);

            if (row == rows - 2 && column == 0 && columns % 4 != 0)
                Shape([(rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 4), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 1)]);

            if (row == rows - 2 && column == 0 && columns % 8 == 4)
                Shape([(rows - 3, 0), (rows - 2, 0), (rows - 1, 0), (0, columns - 2), (0, columns - 1), (1, columns - 1), (2, columns - 1), (3, columns - 1)]);

            if (row == rows + 4 && column == 2 && columns % 8 == 0)
                Shape([(rows - 1, 0), (rows - 1, columns - 1), (0, columns - 3), (0, columns - 2), (0, columns - 1), (1, columns - 3), (1, columns - 2), (1, columns - 1)]);

            do
            {
                if (row < rows && column >= 0 && !read[row, column])
                    Shape([(row - 2, column - 2), (row - 2, column - 1), (row - 1, column - 2), (row - 1, column - 1), (row - 1, column), (row, column - 2), (row, column - 1), (row, column)]);

                row -= 2;
                column += 2;
            }
            while (row >= 0 && column < columns);

            row++;
            column += 3;

            do
            {
                if (row >= 0 && column < columns && !read[row, column])
                    Shape([(row - 2, column - 2), (row - 2, column - 1), (row - 1, column - 2), (row - 1, column - 1), (row - 1, column), (row, column - 2), (row, column - 1), (row, column)]);

                row += 2;
                column -= 2;
            }
            while (row < rows && column >= 0);

            row += 3;
            column++;
        }
        while (row < rows || column < columns);

        FuzzAssert.Equal(count, position, "The number of codewords of the symbol");
        return codewords;
    }

    private static void CheckErrorCorrection(int[] codewords, int dataCodewords, int errorCorrectionPerBlock, int blocks)
    {
        // The generator polynomial (x - α^1)...(x - α^n), highest degree first
        var generator = new int[] { 1 };
        for (var i = 1; i <= errorCorrectionPerBlock; i++)
        {
            var next = new int[generator.Length + 1];
            for (var j = 0; j < generator.Length; j++)
            {
                next[j] ^= generator[j];
                next[j + 1] ^= Multiply(generator[j], Exp[i]);
            }

            generator = next;
        }

        for (var block = 0; block < blocks; block++)
        {
            var remainder = new int[errorCorrectionPerBlock];
            for (var i = block; i < dataCodewords; i += blocks)
            {
                var factor = codewords[i] ^ remainder[0];
                Array.Copy(remainder, 1, remainder, 0, remainder.Length - 1);
                remainder[^1] = 0;
                for (var j = 0; j < remainder.Length; j++)
                {
                    remainder[j] ^= Multiply(generator[j + 1], factor);
                }
            }

            for (var i = 0; i < errorCorrectionPerBlock; i++)
            {
                FuzzAssert.Equal(remainder[i], codewords[dataCodewords + block + (i * blocks)], $"The error correction codeword {i} of block {block}");
            }
        }
    }

    private static int Multiply(int a, int b) => a == 0 || b == 0 ? 0 : Exp[(Log[a] + Log[b]) % 255];

    private static (List<int> Message, int? Eci) DecodeData(ReadOnlySpan<int> codewords)
    {
        var message = new List<int>();
        int? eci = null;
        var upperShift = false;
        var i = 0;

        void Append(int value)
        {
            message.Add(upperShift && value != Fnc1 ? value + 128 : value);
            upperShift = false;
        }

        while (i < codewords.Length)
        {
            var codeword = codewords[i++];
            switch (codeword)
            {
                case >= 1 and <= 128:
                    Append(codeword - 1);
                    break;
                case 129:
                    CheckPadding(codewords, i);
                    FuzzAssert.False(upperShift, "An Upper Shift precedes the padding");
                    return (message, eci);
                case >= 130 and <= 229:
                    Append('0' + ((codeword - 130) / 10));
                    Append('0' + ((codeword - 130) % 10));
                    break;
                case 230 or 239:
                    i = DecodeC40OrText(codewords, i, text: codeword == 239, Append, () => upperShift = true);
                    break;
                case 231:
                    i = DecodeBase256(codewords, i, message);
                    break;
                case 232:
                    Append(Fnc1);
                    break;
                case 235:
                    upperShift = true;
                    break;
                case 238:
                    i = DecodeX12(codewords, i, Append);
                    break;
                case 240:
                    i = DecodeEdifact(codewords, i, Append);
                    break;
                case 241:
                    FuzzAssert.True(message.Count == 0 && eci is null, "An ECI designator is not at the start of the data");
                    var first = codewords[i++];
                    eci = first switch
                    {
                        < 128 => first - 1,
                        < 192 => ((first - 128) * 254) + 127 + codewords[i++] - 1,
                        _ => ((first - 192) * 64516) + 16383 + ((codewords[i++] - 1) * 254) + codewords[i++] - 1,
                    };
                    break;
                default:
                    FuzzAssert.Fail($"The codeword {codeword} is not valid in ASCII");
                    break;
            }
        }

        FuzzAssert.False(upperShift, "An Upper Shift ends the data");
        return (message, eci);
    }

    // The first pad codeword is 129, the next ones are randomized by the 253-state algorithm
    private static void CheckPadding(ReadOnlySpan<int> codewords, int start)
    {
        for (var i = start; i < codewords.Length; i++)
        {
            var pad = 129 + ((149 * (i + 1)) % 253) + 1;
            FuzzAssert.Equal(pad <= 254 ? pad : pad - 254, codewords[i], $"The pad codeword {i}");
        }
    }

    private static int DecodeC40OrText(ReadOnlySpan<int> codewords, int i, bool text, Action<int> append, Action setUpperShift)
    {
        var shift = 0;
        Span<int> values = stackalloc int[3];
        while (i < codewords.Length)
        {
            // A decoder reads the last codeword of the symbol in ASCII
            if (codewords.Length - i == 1 || codewords[i] == 254)
            {
                // A Shift 1 pads the last triplet when two values are left
                FuzzAssert.True(shift is 0 or 1, "A Shift 2 or Shift 3 ends a C40 or Text segment");
                return codewords.Length - i == 1 ? i : i + 1;
            }

            var value = (codewords[i] * 256) + codewords[i + 1] - 1;
            i += 2;
            FuzzAssert.True(value < 64000, $"The C40 or Text triplet {value} is not valid");
            values[0] = value / 1600;
            values[1] = value / 40 % 40;
            values[2] = value % 40;
            foreach (var v in values)
            {
                switch (shift)
                {
                    case 0 when v < 3:
                        shift = v + 1;
                        continue;
                    case 0 when v == 3:
                        append(' ');
                        break;
                    case 0 when v < 14:
                        append('0' + v - 4);
                        break;
                    case 0:
                        append((text ? 'a' : 'A') + v - 14);
                        break;
                    case 1:
                        append(v);
                        break;
                    case 2 when v < 15:
                        append(33 + v);
                        break;
                    case 2 when v < 22:
                        append(58 + v - 15);
                        break;
                    case 2 when v < 27:
                        append(91 + v - 22);
                        break;
                    case 2 when v == 27:
                        append(Fnc1);
                        break;
                    case 2 when v == 30:
                        setUpperShift();
                        break;
                    case 2:
                        FuzzAssert.Fail($"The Shift 2 value {v} is not valid");
                        break;
                    default:
                        append(!text ? 96 + v : v == 0 ? '`' : v < 27 ? 'A' + v - 1 : 123 + v - 27);
                        break;
                }

                shift = 0;
            }
        }

        // The last triplet can end with a Shift 1, as padding
        return i;
    }

    private static int DecodeX12(ReadOnlySpan<int> codewords, int i, Action<int> append)
    {
        Span<int> values = stackalloc int[3];
        while (i < codewords.Length)
        {
            if (codewords.Length - i == 1 || codewords[i] == 254)
                return codewords.Length - i == 1 ? i : i + 1;

            var value = (codewords[i] * 256) + codewords[i + 1] - 1;
            i += 2;
            values[0] = value / 1600;
            values[1] = value / 40 % 40;
            values[2] = value % 40;
            foreach (var v in values)
            {
                append(v switch
                {
                    0 => '\r',
                    1 => '*',
                    2 => '>',
                    3 => ' ',
                    < 14 => '0' + v - 4,
                    _ => 'A' + v - 14,
                });
            }
        }

        return i;
    }

    private static int DecodeEdifact(ReadOnlySpan<int> codewords, int i, Action<int> append)
    {
        var bit = i * 8;
        int ReadBits(ReadOnlySpan<int> codewords, int count)
        {
            var value = 0;
            for (var k = 0; k < count; k++, bit++)
            {
                value = (value << 1) | ((codewords[bit / 8] >> (7 - (bit % 8))) & 1);
            }

            return value;
        }

        // A decoder returns to ASCII when two codewords or less are left
        while ((codewords.Length * 8) - bit > 16)
        {
            for (var k = 0; k < 4; k++)
            {
                var value = ReadBits(codewords, 6);
                if (value == 31)
                {
                    var rest = (8 - (bit % 8)) % 8;
                    FuzzAssert.Equal(0, ReadBits(codewords, rest), "The bits after an EDIFACT unlatch");
                    return bit / 8;
                }

                append((value & 0x20) == 0 ? value | 0x40 : value);
            }
        }

        return bit / 8;
    }

    private static int DecodeBase256(ReadOnlySpan<int> codewords, int i, List<int> message)
    {
        int Next(ReadOnlySpan<int> codewords, ref int i)
        {
            FuzzAssert.True(i < codewords.Length, "A Base 256 field ends after the symbol");
            var value = codewords[i] - (((149 * (i + 1)) % 255) + 1);
            i++;
            return value >= 0 ? value : value + 256;
        }

        var length = Next(codewords, ref i);
        length = length switch
        {
            0 => codewords.Length - i,
            < 250 => length,
            _ => (250 * (length - 249)) + Next(codewords, ref i),
        };
        for (var k = 0; k < length; k++)
        {
            message.Add(Next(codewords, ref i));
        }

        return i;
    }
}
