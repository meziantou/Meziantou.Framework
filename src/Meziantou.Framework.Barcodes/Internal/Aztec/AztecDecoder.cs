namespace Meziantou.Framework.Barcodes.Internal.Aztec;

/// <summary>
/// Reads an Aztec symbol (ISO/IEC 24778): its orientation from the orientation marks of the finder, the mode message
/// (layers and data codewords) with its error correction over GF(16), the codewords of the layers with their error
/// correction, the bit stuffing, and the character modes of the data.
/// </summary>
internal static class AztecDecoder
{
    private const int ModeUpper = 0;
    private const int ModeLower = 1;
    private const int ModeDigit = 2;
    private const int ModeMixed = 3;
    private const int ModePunct = 4;

    // The characters of the codes 1 to 27 of the Mixed mode, and 6 to 30 of the Punct mode
    private static ReadOnlySpan<byte> MixedCharacters => [0x20, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F, (byte)'@', (byte)'\\', (byte)'^', (byte)'_', (byte)'`', (byte)'|', (byte)'~', 0x7F];

    private static ReadOnlySpan<byte> PunctCharacters => "!\"#$%&'()*+,-./:;<=>?[]{}"u8;

    /// <summary>Decodes a symbol in any of its 8 orientations.</summary>
    /// <returns>The reason of the failure, or <see langword="null"/>.</returns>
    public static string? TryDecode(bool[,] modules, out DecodedDataBuilder? data, out int correctedErrorCount, out bool isMirrored)
    {
        data = null;
        correctedErrorCount = 0;
        isMirrored = false;
        var size = modules.GetLength(0);
        if (size != modules.GetLength(1))
            return $"A matrix of {modules.GetLength(0)}x{modules.GetLength(1)} modules is not the size of an Aztec symbol.";

        // The orientations whose orientation marks match best first: a compact symbol and a full-range symbol can have the same size
        var oriented = new bool[MatrixTransforms.Count][,];
        var candidates = new List<(int Score, int Transform, bool Compact)>();
        foreach (var compact in (ReadOnlySpan<bool>)[true, false])
        {
            if (!IsSize(size, compact))
                continue;

            for (var transform = 0; transform < MatrixTransforms.Count; transform++)
            {
                oriented[transform] ??= MatrixTransforms.Apply(modules, transform);
                candidates.Add((GetOrientationScore(oriented[transform], compact), transform, compact));
            }
        }

        if (candidates.Count is 0)
            return $"A matrix of {size}x{size} modules is not the size of an Aztec symbol.";

        string? error = null;
        foreach (var (score, transform, compact) in candidates.OrderByDescending(candidate => candidate.Score))
        {
            if (score < 9)
                break;

            // Report the failure of the orientation that matches best
            var builder = new DecodedDataBuilder();
            var failure = TryDecodeOriented(oriented[transform], compact, builder, out correctedErrorCount);
            if (failure is null)
            {
                data = builder;
                isMirrored = MatrixTransforms.IsMirrored(transform);
                return null;
            }

            error ??= failure;
        }

        return error ?? "The orientation marks of the Aztec symbol cannot be found.";
    }

    private static bool IsSize(int size, bool compact)
    {
        for (var layers = 1; layers <= (compact ? 4 : 32); layers++)
        {
            if (AztecEncoder.GetSize(compact, layers) == size)
                return true;
        }

        return false;
    }

    // The orientation marks at the corners of the mode message ring: 3 dark modules at the top left, 2 at the top right,
    // 1 at the bottom right, none at the bottom left
    private static int GetOrientationScore(bool[,] modules, bool compact)
    {
        var size = modules.GetLength(0);
        var c = size / 2;
        var r = compact ? 5 : 7;
        ReadOnlySpan<(int X, int Y, bool Dark)> marks =
        [
            (c - r, c - r, true), (c - r + 1, c - r, true), (c - r, c - r + 1, true),
            (c + r, c - r, true), (c + r, c - r + 1, true), (c + r - 1, c - r, false),
            (c + r, c + r - 1, true), (c + r, c + r, false), (c + r - 1, c + r, false),
            (c - r, c + r, false), (c - r + 1, c + r, false), (c - r, c + r - 1, false),
        ];

        var score = 0;
        foreach (var (x, y, dark) in marks)
        {
            if (Get(modules, x, y) == dark)
            {
                score++;
            }
        }

        return score;
    }

    private static string? TryDecodeOriented(bool[,] modules, bool compact, DecodedDataBuilder data, out int correctedErrorCount)
    {
        correctedErrorCount = 0;
        var size = modules.GetLength(0);
        var center = size / 2;

        // The mode message goes clockwise around the finder from the top left (ISO/IEC 24778, 7.2)
        var modeBits = new bool[compact ? 28 : 40];
        if (compact)
        {
            for (var i = 0; i < 7; i++)
            {
                var offset = center - 3 + i;
                modeBits[i] = Get(modules, offset, center - 5);
                modeBits[i + 7] = Get(modules, center + 5, offset);
                modeBits[20 - i] = Get(modules, offset, center + 5);
                modeBits[27 - i] = Get(modules, center - 5, offset);
            }
        }
        else
        {
            for (var i = 0; i < 10; i++)
            {
                var offset = center - 5 + i + (i / 5);
                modeBits[i] = Get(modules, offset, center - 7);
                modeBits[i + 10] = Get(modules, center + 7, offset);
                modeBits[29 - i] = Get(modules, offset, center + 7);
                modeBits[39 - i] = Get(modules, center - 7, offset);
            }
        }

        var modeWords = new int[modeBits.Length / 4];
        for (var i = 0; i < modeWords.Length; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                modeWords[i] = (modeWords[i] << 1) | (modeBits[(i * 4) + j] ? 1 : 0);
            }
        }

        var modeCorrected = ReedSolomonDecoder.Correct(BinaryGaloisField.Aztec16, modeWords, compact ? 5 : 6, firstRoot: 1);
        if (modeCorrected < 0)
            return "The mode message of the Aztec symbol cannot be read.";

        int layers;
        int dataWordCount;
        if (compact)
        {
            layers = (modeWords[0] >> 2) + 1;
            dataWordCount = (((modeWords[0] & 3) << 4) | modeWords[1]) + 1;
        }
        else
        {
            layers = ((modeWords[0] << 1) | (modeWords[1] >> 3)) + 1;
            dataWordCount = (((modeWords[1] & 7) << 8) | (modeWords[2] << 4) | modeWords[3]) + 1;
        }

        if (AztecEncoder.GetSize(compact, layers) != size)
            return "The mode message of the Aztec symbol does not match its size.";

        var wordSize = AztecEncoder.GetWordSize(layers);
        var totalBits = AztecEncoder.GetTotalBits(compact, layers);
        var totalWords = totalBits / wordSize;
        if (dataWordCount >= totalWords)
            return "The mode message of the Aztec symbol has more data codewords than the symbol holds.";

        var messageBits = ReadMessageBits(modules, compact, layers, totalBits);
        var words = new int[totalWords];
        var bitIndex = totalBits % wordSize;
        for (var i = 0; i < totalWords; i++)
        {
            for (var j = 0; j < wordSize; j++)
            {
                words[i] = (words[i] << 1) | (messageBits[bitIndex++] ? 1 : 0);
            }
        }

        correctedErrorCount = ReedSolomonDecoder.Correct(AztecEncoder.GetField(wordSize), words, totalWords - dataWordCount, firstRoot: 1);
        if (correctedErrorCount < 0)
            return "The Aztec symbol has more errors than the error correction can fix.";

        correctedErrorCount += modeCorrected;

        // A codeword whose first bits are all 0 or all 1 has a stuffed bit last (ISO/IEC 24778, 7.3.1.2)
        var dataBits = new List<bool>(dataWordCount * wordSize);
        var stuffedOnes = (1 << wordSize) - 2;
        for (var i = 0; i < dataWordCount; i++)
        {
            var word = words[i];
            if (word == 0 || word == (1 << wordSize) - 1)
                return "The Aztec symbol has a codeword that is not valid.";

            var count = word is 1 || word == stuffedOnes ? wordSize - 1 : wordSize;
            for (var j = 0; j < count; j++)
            {
                dataBits.Add(((word >> (wordSize - 1 - j)) & 1) != 0);
            }
        }

        return DecodeBits(dataBits, data);
    }

    // The layers, from the outermost one, are 2-module wide strips that go clockwise around the core
    private static bool[] ReadMessageBits(bool[,] modules, bool compact, int layers, int totalBits)
    {
        var bits = new bool[totalBits];
        var baseSize = (compact ? 11 : 14) + (layers * 4);
        var alignmentMap = AztecEncoder.GetAlignmentMap(compact, layers);
        for (int i = 0, rowOffset = 0; i < layers; i++)
        {
            var rowSize = ((layers - i) * 4) + (compact ? 9 : 12);
            for (var j = 0; j < rowSize; j++)
            {
                var columnOffset = j * 2;
                for (var k = 0; k < 2; k++)
                {
                    bits[rowOffset + columnOffset + k] = Get(modules, alignmentMap[(i * 2) + k], alignmentMap[(i * 2) + j]);
                    bits[rowOffset + (rowSize * 2) + columnOffset + k] = Get(modules, alignmentMap[(i * 2) + j], alignmentMap[baseSize - 1 - (i * 2) - k]);
                    bits[rowOffset + (rowSize * 4) + columnOffset + k] = Get(modules, alignmentMap[baseSize - 1 - (i * 2) - k], alignmentMap[baseSize - 1 - (i * 2) - j]);
                    bits[rowOffset + (rowSize * 6) + columnOffset + k] = Get(modules, alignmentMap[baseSize - 1 - (i * 2) - j], alignmentMap[(i * 2) + k]);
                }
            }

            rowOffset += rowSize * 8;
        }

        return bits;
    }

    private static bool Get(bool[,] modules, int x, int y) => modules[y, x];

    // The character modes (ISO/IEC 24778, Table 2). The data ends when the bits left do not make a complete code: the
    // last codeword is padded with 1 bits.
    private static string? DecodeBits(List<bool> bits, DecodedDataBuilder data)
    {
        var position = 0;
        var mode = ModeUpper;
        int? shift = null;
        while (true)
        {
            var current = shift ?? mode;
            shift = null;
            var code = Read(current == ModeDigit ? 4 : 5);
            if (code < 0)
                return null;

            switch (current)
            {
                case ModeUpper or ModeLower:
                    switch (code)
                    {
                        case 0:
                            shift = ModePunct;
                            break;
                        case 1:
                            data.AppendByte(' ');
                            break;
                        case < 28:
                            data.AppendByte((current == ModeUpper ? 'A' : 'a') + code - 2);
                            break;
                        case 28:
                            if (current == ModeUpper)
                            {
                                mode = ModeLower;
                            }
                            else
                            {
                                shift = ModeUpper;
                            }

                            break;
                        case 29:
                            mode = ModeMixed;
                            break;
                        case 30:
                            mode = ModeDigit;
                            break;
                        default:
                            if (!ReadBinaryShift())
                                return null;

                            break;
                    }

                    break;

                case ModeMixed:
                    switch (code)
                    {
                        case 0:
                            shift = ModePunct;
                            break;
                        case < 28:
                            data.AppendByte(MixedCharacters[code - 1]);
                            break;
                        case 28:
                            mode = ModeLower;
                            break;
                        case 29:
                            mode = ModeUpper;
                            break;
                        case 30:
                            mode = ModePunct;
                            break;
                        default:
                            if (!ReadBinaryShift())
                                return null;

                            break;
                    }

                    break;

                case ModePunct:
                    switch (code)
                    {
                        case 0:
                            var error = ReadFlag();
                            if (error is not null)
                                return error;

                            break;
                        case 1:
                            data.AppendByte('\r');
                            break;
                        case 2:
                            data.AppendByte('\r');
                            data.AppendByte('\n');
                            break;
                        case 3 or 4 or 5:
                            data.AppendByte(code switch { 3 => '.', 4 => ',', _ => ':' });
                            data.AppendByte(' ');
                            break;
                        case < 31:
                            data.AppendByte(PunctCharacters[code - 6]);
                            break;
                        default:
                            mode = ModeUpper;
                            break;
                    }

                    break;

                default:
                    switch (code)
                    {
                        case 0:
                            shift = ModePunct;
                            break;
                        case 1:
                            data.AppendByte(' ');
                            break;
                        case < 12:
                            data.AppendByte('0' + code - 2);
                            break;
                        case 12:
                            data.AppendByte(',');
                            break;
                        case 13:
                            data.AppendByte('.');
                            break;
                        case 14:
                            mode = ModeUpper;
                            break;
                        default:
                            shift = ModeUpper;
                            break;
                    }

                    break;
            }
        }

        int Read(int count)
        {
            if (position + count > bits.Count)
                return -1;

            var value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | (bits[position++] ? 1 : 0);
            }

            return value;
        }

        // B/S: a 5-bit length, or 0 and an 11-bit length minus 31, then the bytes
        bool ReadBinaryShift()
        {
            var length = Read(5);
            if (length is 0)
            {
                length = Read(11);
                if (length < 0)
                    return false;

                length += 31;
            }

            if (length < 0)
                return false;

            for (var i = 0; i < length; i++)
            {
                var value = Read(8);
                if (value < 0)
                    return false;

                data.AppendByte(value);
            }

            return true;
        }

        // FLG(n): FNC1 for n = 0, an ECI designator of n digits for n = 1 to 6
        string? ReadFlag()
        {
            var n = Read(3);
            if (n < 0)
                return null;

            if (n is 0)
            {
                data.AppendFnc1();
                return null;
            }

            if (n is 7)
                return "The Aztec symbol has a reserved FLG(7) character.";

            var eci = 0;
            for (var i = 0; i < n; i++)
            {
                var digit = Read(4);
                if (digit is < 2 or > 11)
                    return "The ECI designator of the Aztec symbol is not valid.";

                eci = (eci * 10) + digit - 2;
            }

            data.SetEci(eci);
            return null;
        }
    }
}
