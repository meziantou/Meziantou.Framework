namespace Meziantou.Framework.Barcodes.Internal.DataMatrix;

/// <summary>
/// Reads a Data Matrix (ECC 200) symbol: the codewords of its data regions (ISO/IEC 16022, Annex F), the Reed-Solomon
/// error correction of its blocks, and the encodation schemes of its data (5.2), which end the way the encoder expects
/// (a C40, Text or X12 segment returns to ASCII when one codeword is left, an EDIFACT segment when two or less are left).
/// </summary>
internal static class DataMatrixDecoder
{
    private const int Pad = 129;
    private const int Unlatch = 254;

    /// <summary>Decodes a symbol in any of its 8 orientations: the solid sides of the finder pattern go to the left and the bottom.</summary>
    /// <returns>The reason of the failure, or <see langword="null"/>.</returns>
    public static string? TryDecodeAnyOrientation(bool[,] modules, out DecodedDataBuilder? data, out bool isGs1, out int correctedErrorCount)
    {
        data = null;
        isGs1 = false;
        correctedErrorCount = 0;
        var candidates = new List<(double Score, int Transform)>();
        for (var transform = 0; transform < MatrixTransforms.Count; transform++)
        {
            var (height, width) = MatrixTransforms.GetSize(modules, transform);
            if (DataMatrixSymbolSize.All.Any(size => size.Rows == height && size.Columns == width))
            {
                candidates.Add((GetFinderScore(modules, transform), transform));
            }
        }

        if (candidates.Count is 0)
            return $"A matrix of {modules.GetLength(0)}x{modules.GetLength(1)} modules is not the size of a Data Matrix symbol.";

        string? error = null;
        foreach (var (score, transform) in candidates.OrderByDescending(candidate => candidate.Score))
        {
            if (score < 0.75)
                break;

            // Report the failure of the orientation that matches best
            var builder = new DecodedDataBuilder();
            var failure = TryDecode(MatrixTransforms.Apply(modules, transform), builder, out isGs1, out correctedErrorCount);
            if (failure is null)
            {
                data = builder;
                return null;
            }

            error ??= failure;
        }

        return error ?? "The finder pattern of the Data Matrix symbol cannot be found.";
    }

    // The share of the modules of the outer finder pattern that match: solid on the left and at the bottom, alternating
    // at the top (dark first) and on the right (light first)
    private static double GetFinderScore(bool[,] modules, int transform)
    {
        var oriented = MatrixTransforms.Apply(modules, transform);
        var height = oriented.GetLength(0);
        var width = oriented.GetLength(1);
        var matches = 0;
        for (var row = 0; row < height; row++)
        {
            matches += oriented[row, 0] ? 1 : 0;
            matches += oriented[row, width - 1] == (row % 2 is 1 || row == height - 1) ? 1 : 0;
        }

        for (var column = 0; column < width; column++)
        {
            matches += oriented[height - 1, column] ? 1 : 0;
            matches += oriented[0, column] == (column % 2 is 0) ? 1 : 0;
        }

        return matches / (double)(2 * (height + width));
    }

    /// <summary>Decodes a symbol in its orientation (the solid sides of the finder pattern on the left and at the bottom).</summary>
    /// <returns>The reason of the failure, or <see langword="null"/>.</returns>
    public static string? TryDecode(bool[,] modules, DecodedDataBuilder data, out bool isGs1, out int correctedErrorCount)
    {
        isGs1 = false;
        correctedErrorCount = 0;
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        var size = DataMatrixSymbolSize.All.FirstOrDefault(size => size.Rows == height && size.Columns == width);
        if (size is null)
            return $"A matrix of {height}x{width} modules is not the size of a Data Matrix symbol.";

        // The mapping matrix is the data regions without their finder patterns
        var rows = size.RegionRows * size.VerticalRegions;
        var columns = size.RegionColumns * size.HorizontalRegions;
        var mapping = new bool[rows, columns];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var symbolRow = (row / size.RegionRows * (size.RegionRows + 2)) + (row % size.RegionRows) + 1;
                var symbolColumn = (column / size.RegionColumns * (size.RegionColumns + 2)) + (column % size.RegionColumns) + 1;
                mapping[row, column] = modules[symbolRow, symbolColumn];
            }
        }

        var codewords = DataMatrixPlacement.Read(mapping, size.DataCodewords + size.ErrorCorrectionCodewords);

        // Codeword i of the data is in block i modulo the number of blocks, and so are the error correction codewords
        var blocks = size.Blocks;
        var errorCorrectionPerBlock = size.ErrorCorrectionCodewords / blocks;
        for (var block = 0; block < blocks; block++)
        {
            var dataCount = (size.DataCodewords - block + blocks - 1) / blocks;
            var blockCodewords = new int[dataCount + errorCorrectionPerBlock];
            for (var i = 0; i < dataCount; i++)
            {
                blockCodewords[i] = codewords[block + (i * blocks)];
            }

            for (var i = 0; i < errorCorrectionPerBlock; i++)
            {
                blockCodewords[dataCount + i] = codewords[size.DataCodewords + block + (i * blocks)];
            }

            var corrected = ReedSolomonDecoder.Correct(BinaryGaloisField.Field256, blockCodewords, errorCorrectionPerBlock, firstRoot: 1);
            if (corrected < 0)
                return "The Data Matrix symbol has more errors than the error correction can fix.";

            correctedErrorCount += corrected;
            for (var i = 0; i < dataCount; i++)
            {
                codewords[block + (i * blocks)] = blockCodewords[i];
            }
        }

        return DecodeData(codewords.AsSpan(0, size.DataCodewords), data, out isGs1);
    }

    private static string? DecodeData(ReadOnlySpan<int> codewords, DecodedDataBuilder data, out bool isGs1)
    {
        isGs1 = codewords.Length > 0 && codewords[0] is 232;
        var upperShift = false;
        string? trailer = null;
        var i = isGs1 ? 1 : 0;
        while (i < codewords.Length)
        {
            var codeword = codewords[i++];
            string? error = null;
            switch (codeword)
            {
                case >= 1 and <= 128:
                    Append(codeword - 1);
                    break;

                case Pad:
                    i = codewords.Length;
                    break;

                case >= 130 and <= 229:
                    Append('0' + ((codeword - 130) / 10));
                    Append('0' + ((codeword - 130) % 10));
                    break;

                case 230 or 239:
                    error = DecodeC40OrText(codewords, ref i, text: codeword is 239, data, ref upperShift);
                    break;

                case 231:
                    error = DecodeBase256(codewords, ref i, data);
                    break;

                case 232:
                    data.AppendFnc1();
                    break;

                case 235:
                    upperShift = true;
                    break;

                // Macro 05 and Macro 06: a header and a trailer around the data (ISO/IEC 16022, 5.2.4.1)
                case 236 or 237 when data.ByteCount is 0:
                    data.AppendText(codeword is 236 ? "[)>\u001E05\u001D" : "[)>\u001E06\u001D");
                    trailer = "\u001E\u0004";
                    break;

                case 238:
                    error = DecodeX12(codewords, ref i, data);
                    break;

                case 240:
                    error = DecodeEdifact(codewords, ref i, data);
                    break;

                case 241:
                    var eci = ReadEci(codewords, ref i);
                    if (eci < 0)
                        return "The ECI designator of the Data Matrix symbol is not valid.";

                    data.SetEci(eci);
                    break;

                default:
                    return $"The codeword {codeword} is not supported in the ASCII encodation of a Data Matrix symbol.";
            }

            if (error is not null)
                return error;
        }

        if (trailer is not null)
        {
            data.AppendText(trailer);
        }

        return null;

        void Append(int value)
        {
            data.AppendByte(upperShift ? value + 128 : value);
            upperShift = false;
        }
    }

    private static int ReadEci(ReadOnlySpan<int> codewords, ref int i)
    {
        if (i >= codewords.Length)
            return -1;

        var first = codewords[i++];
        if (first is < 1 or > 254)
            return -1;

        if (first < 128)
            return first - 1;

        if (first < 192)
        {
            if (i >= codewords.Length)
                return -1;

            return ((first - 128) * 254) + 127 + codewords[i++] - 1;
        }

        if (i + 1 >= codewords.Length)
            return -1;

        var value = ((first - 192) * 64516) + 16383 + ((codewords[i] - 1) * 254) + codewords[i + 1] - 1;
        i += 2;
        return value;
    }

    private static string? DecodeC40OrText(ReadOnlySpan<int> codewords, ref int i, bool text, DecodedDataBuilder data, ref bool upperShift)
    {
        var shift = 0;
        Span<int> values = stackalloc int[3];
        while (i < codewords.Length)
        {
            if (codewords[i] == Unlatch)
            {
                i++;
                return null;
            }

            // The last codeword of the symbol is read in ASCII, when it is not an unlatch
            if (codewords.Length - i == 1)
                return null;

            var value = (codewords[i] * 256) + codewords[i + 1] - 1;
            i += 2;
            if (value is < 0 or >= 64000)
                return "A C40 or Text triplet of the Data Matrix symbol is not valid.";

            values[0] = value / 1600;
            values[1] = value / 40 % 40;
            values[2] = value % 40;
            foreach (var v in values)
            {
                if (shift is 0)
                {
                    switch (v)
                    {
                        case < 3:
                            shift = v + 1;
                            continue;
                        case 3:
                            Append(' ', ref upperShift);
                            break;
                        case < 14:
                            Append('0' + v - 4, ref upperShift);
                            break;
                        default:
                            Append((text ? 'a' : 'A') + v - 14, ref upperShift);
                            break;
                    }

                    continue;
                }

                switch (shift)
                {
                    case 1:
                        Append(v, ref upperShift);
                        break;
                    case 2 when v < 15:
                        Append(33 + v, ref upperShift);
                        break;
                    case 2 when v < 22:
                        Append(58 + v - 15, ref upperShift);
                        break;
                    case 2 when v < 27:
                        Append(91 + v - 22, ref upperShift);
                        break;
                    case 2 when v == 27:
                        data.AppendFnc1();
                        break;
                    case 2 when v == 30:
                        upperShift = true;
                        break;
                    case 2:
                        return "A C40 or Text value of the Data Matrix symbol is not valid.";
                    default:
                        Append(!text ? 96 + v : v == 0 ? '`' : v < 27 ? 'A' + v - 1 : 123 + v - 27, ref upperShift);
                        break;
                }

                shift = 0;
            }
        }

        return null;

        void Append(int value, ref bool upperShift)
        {
            data.AppendByte(upperShift ? value + 128 : value);
            upperShift = false;
        }
    }

    private static string? DecodeX12(ReadOnlySpan<int> codewords, ref int i, DecodedDataBuilder data)
    {
        Span<int> values = stackalloc int[3];
        while (i < codewords.Length)
        {
            if (codewords[i] == Unlatch)
            {
                i++;
                return null;
            }

            if (codewords.Length - i == 1)
                return null;

            var value = (codewords[i] * 256) + codewords[i + 1] - 1;
            i += 2;
            if (value is < 0 or >= 64000)
                return "An X12 triplet of the Data Matrix symbol is not valid.";

            values[0] = value / 1600;
            values[1] = value / 40 % 40;
            values[2] = value % 40;
            foreach (var v in values)
            {
                data.AppendByte(v switch
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

        return null;
    }

    private static string? DecodeEdifact(ReadOnlySpan<int> codewords, ref int i, DecodedDataBuilder data)
    {
        var bit = i * 8;

        // The decoder returns to ASCII when two codewords or less are left
        while ((codewords.Length * 8) - bit > 16)
        {
            for (var k = 0; k < 4; k++)
            {
                var value = ReadBits(codewords, ref bit, 6);
                if (value == 31)
                {
                    // The unlatch ends the segment; the rest of its codeword is padding
                    i = (bit + 7) / 8;
                    return null;
                }

                data.AppendByte((value & 0x20) == 0 ? value | 0x40 : value);
            }
        }

        i = (bit + 7) / 8;
        return null;

        static int ReadBits(ReadOnlySpan<int> codewords, ref int bit, int count)
        {
            var value = 0;
            for (var k = 0; k < count; k++, bit++)
            {
                value = (value << 1) | ((codewords[bit / 8] >> (7 - (bit % 8))) & 1);
            }

            return value;
        }
    }

    private static string? DecodeBase256(ReadOnlySpan<int> codewords, ref int i, DecodedDataBuilder data)
    {
        // The 255-state algorithm randomizes the codewords (ISO/IEC 16022, Annex B)
        if (i >= codewords.Length)
            return "A Base 256 field of the Data Matrix symbol ends after the symbol.";

        var length = Next(codewords, ref i);
        if (length >= 250)
        {
            if (i >= codewords.Length)
                return "A Base 256 field of the Data Matrix symbol ends after the symbol.";

            length = (250 * (length - 249)) + Next(codewords, ref i);
        }
        else if (length is 0)
        {
            length = codewords.Length - i;
        }

        if (i + length > codewords.Length)
            return "A Base 256 field of the Data Matrix symbol ends after the symbol.";

        for (var k = 0; k < length; k++)
        {
            data.AppendByte(Next(codewords, ref i));
        }

        return null;

        static int Next(ReadOnlySpan<int> codewords, ref int i)
        {
            var value = codewords[i] - (((149 * (i + 1)) % 255) + 1);
            i++;
            return value >= 0 ? value : value + 256;
        }
    }
}
