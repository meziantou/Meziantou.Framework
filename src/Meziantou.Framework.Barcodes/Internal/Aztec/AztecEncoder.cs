namespace Meziantou.Framework.Barcodes.Internal.Aztec;

// ISO/IEC 24778:2008
internal static class AztecEncoder
{
    private const int MaxCompactLayers = 4;
    private const int MaxLayers = 32;
    private const int MaxCompactDataCodewords = 64; // 6 bits in the mode message
    private const int MaxDataCodewords = 2048; // 11 bits in the mode message

    /// <summary>Encodes the data and returns the module matrix.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <param name="eci">The ECI assignment number to declare at the start of the data, or <see langword="null"/>.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    public static bool[,] Encode(ReadOnlySpan<byte> data, int? eci, AztecOptions? options)
    {
        var errorCorrectionPercent = options?.ErrorCorrectionPercent ?? 23;
        var compactOption = options?.Compact;
        var layersOption = options?.Layers;
        if (compactOption is true && layersOption > MaxCompactLayers)
            throw new ArgumentException($"A compact Aztec symbol has 1 to {MaxCompactLayers} layers.", nameof(options));

        var bits = AztecHighLevelEncoder.Encode(data, eci);

        int[]? dataWords = null;
        var dataWordSize = 0;
        foreach (var (compact, layers) in GetCandidates(compactOption, layersOption))
        {
            var wordSize = GetWordSize(layers);
            if (dataWords is null || dataWordSize != wordSize)
            {
                dataWords = StuffBits(bits, wordSize);
                dataWordSize = wordSize;
            }

            var totalWords = GetTotalBits(compact, layers) / wordSize;
            if (dataWords.Length > (compact ? MaxCompactDataCodewords : MaxDataCodewords))
                continue;

            // ISO/IEC 24778 expresses the error correction as a percentage of the symbol capacity, plus 3 codewords
            var minimumErrorCorrectionWords = (((totalWords * errorCorrectionPercent) + 99) / 100) + 3;
            if (dataWords.Length + minimumErrorCorrectionWords > totalWords)
                continue;

            return CreateMatrix(compact, layers, dataWords, wordSize);
        }

        throw new InvalidOperationException(layersOption is null
            ? "The data is too long for an Aztec symbol with the requested error correction."
            : "The data is too long for an Aztec symbol of the requested number of layers with the requested error correction.");
    }

    // The allowed sizes in increasing order. A compact symbol comes before the full-range symbol of the same size, which
    // holds less data.
    private static List<(bool Compact, int Layers)> GetCandidates(bool? compact, int? layers)
    {
        var candidates = new List<(bool Compact, int Layers)>();
        for (var i = 1; i <= MaxLayers; i++)
        {
            if (layers is not null && layers != i)
                continue;

            if (compact is not false && i <= MaxCompactLayers)
            {
                candidates.Add((true, i));
            }

            if (compact is not true)
            {
                candidates.Add((false, i));
            }
        }

        candidates.Sort((a, b) => GetSize(a.Compact, a.Layers) != GetSize(b.Compact, b.Layers)
            ? GetSize(a.Compact, a.Layers).CompareTo(GetSize(b.Compact, b.Layers))
            : b.Compact.CompareTo(a.Compact));
        return candidates;
    }

    internal static int GetWordSize(int layers) => layers switch
    {
        <= 2 => 6,
        <= 8 => 8,
        <= 22 => 10,
        _ => 12,
    };

    internal static int GetTotalBits(bool compact, int layers) => ((compact ? 88 : 112) + (16 * layers)) * layers;

    // The number of modules on a side, including the reference grid of a full-range symbol
    internal static int GetSize(bool compact, int layers)
    {
        if (compact)
            return 11 + (4 * layers);

        var baseSize = 14 + (4 * layers);
        return baseSize + 1 + (2 * (((baseSize / 2) - 1) / 15));
    }

    // The positions of the modules without the reference grid, mapped to the positions in the symbol
    internal static int[] GetAlignmentMap(bool compact, int layers)
    {
        var baseSize = (compact ? 11 : 14) + (layers * 4);
        var alignmentMap = new int[baseSize];
        if (compact)
        {
            for (var i = 0; i < alignmentMap.Length; i++)
            {
                alignmentMap[i] = i;
            }

            return alignmentMap;
        }

        var originalCenter = baseSize / 2;
        var center = GetSize(compact, layers) / 2;
        for (var i = 0; i < originalCenter; i++)
        {
            var offset = i + (i / 15);
            alignmentMap[originalCenter - i - 1] = center - offset - 1;
            alignmentMap[originalCenter + i] = center + offset + 1;
        }

        return alignmentMap;
    }

    internal static BinaryGaloisField GetField(int wordSize) => wordSize switch
    {
        4 => BinaryGaloisField.Aztec16,
        6 => BinaryGaloisField.Aztec64,
        8 => BinaryGaloisField.Field256,
        10 => BinaryGaloisField.Aztec1024,
        _ => BinaryGaloisField.Aztec4096,
    };

    // Splits the bits in codewords. A codeword whose first wordSize - 1 bits are all 0 or all 1 gets the opposite bit
    // last, and the next codeword starts with the bit that did not fit. The last codeword is padded with 1 bits, and its
    // last bit is 0 when it is all 1s.
    internal static int[] StuffBits(AztecBitList bits, int wordSize)
    {
        var words = new List<int>((bits.Count / wordSize) + 2);
        var mask = (1 << wordSize) - 2;
        var index = 0;
        while (index < bits.Count)
        {
            var word = 0;
            for (var j = 0; j < wordSize; j++)
            {
                if (index + j >= bits.Count || bits[index + j])
                {
                    word |= 1 << (wordSize - 1 - j);
                }
            }

            if ((word & mask) == mask)
            {
                words.Add(word & mask);
                index += wordSize - 1;
            }
            else if ((word & mask) == 0)
            {
                words.Add(word | 1);
                index += wordSize - 1;
            }
            else
            {
                words.Add(word);
                index += wordSize;
            }
        }

        return [.. words];
    }

    private static bool[,] CreateMatrix(bool compact, int layers, int[] dataWords, int wordSize)
    {
        var totalBits = GetTotalBits(compact, layers);
        var totalWords = totalBits / wordSize;
        var words = new int[totalWords];
        dataWords.CopyTo(words, 0);
        GetField(wordSize).ComputeErrorCorrection(dataWords, words.AsSpan(dataWords.Length));

        // The bits that do not make a whole codeword are 0s at the start
        var messageBits = new AztecBitList();
        messageBits.Append(0, totalBits % wordSize);
        foreach (var word in words)
        {
            messageBits.Append(word, wordSize);
        }

        var modeMessage = CreateModeMessage(compact, layers, dataWords.Length);

        var baseSize = (compact ? 11 : 14) + (layers * 4);
        var size = GetSize(compact, layers);
        var alignmentMap = GetAlignmentMap(compact, layers);
        var matrix = new bool[size, size];

        // The layers, from the outermost one, are made of 2-module wide strips that go clockwise around the core,
        // starting at the top left corner
        for (int i = 0, rowOffset = 0; i < layers; i++)
        {
            var rowSize = ((layers - i) * 4) + (compact ? 9 : 12);
            for (var j = 0; j < rowSize; j++)
            {
                var columnOffset = j * 2;
                for (var k = 0; k < 2; k++)
                {
                    if (messageBits[rowOffset + columnOffset + k])
                    {
                        Set(matrix, alignmentMap[(i * 2) + k], alignmentMap[(i * 2) + j]);
                    }

                    if (messageBits[rowOffset + (rowSize * 2) + columnOffset + k])
                    {
                        Set(matrix, alignmentMap[(i * 2) + j], alignmentMap[baseSize - 1 - (i * 2) - k]);
                    }

                    if (messageBits[rowOffset + (rowSize * 4) + columnOffset + k])
                    {
                        Set(matrix, alignmentMap[baseSize - 1 - (i * 2) - k], alignmentMap[baseSize - 1 - (i * 2) - j]);
                    }

                    if (messageBits[rowOffset + (rowSize * 6) + columnOffset + k])
                    {
                        Set(matrix, alignmentMap[baseSize - 1 - (i * 2) - j], alignmentMap[(i * 2) + k]);
                    }
                }
            }

            rowOffset += rowSize * 8;
        }

        DrawModeMessage(matrix, compact, size, modeMessage);
        var middle = size / 2;
        if (compact)
        {
            DrawBullsEye(matrix, middle, 5);
        }
        else
        {
            DrawBullsEye(matrix, middle, 7);

            // The reference grid: every 16th row and column from the center has alternating modules
            for (int i = 0, j = 0; i < (baseSize / 2) - 1; i += 15, j += 16)
            {
                for (var k = middle & 1; k < size; k += 2)
                {
                    Set(matrix, middle - j, k);
                    Set(matrix, middle + j, k);
                    Set(matrix, k, middle - j);
                    Set(matrix, k, middle + j);
                }
            }
        }

        return matrix;
    }

    // The number of layers and of data codewords, followed by their Reed-Solomon check words over GF(16)
    private static AztecBitList CreateModeMessage(bool compact, int layers, int dataWordCount)
    {
        var bits = new AztecBitList();
        if (compact)
        {
            bits.Append(layers - 1, 2);
            bits.Append(dataWordCount - 1, 6);
        }
        else
        {
            bits.Append(layers - 1, 5);
            bits.Append(dataWordCount - 1, 11);
        }

        var dataWords = new int[bits.Count / 4];
        for (var i = 0; i < dataWords.Length; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                dataWords[i] = (dataWords[i] << 1) | (bits[(i * 4) + j] ? 1 : 0);
            }
        }

        Span<int> checkWords = stackalloc int[compact ? 5 : 6];
        BinaryGaloisField.Aztec16.ComputeErrorCorrection(dataWords, checkWords);
        foreach (var word in checkWords)
        {
            bits.Append(word, 4);
        }

        return bits;
    }

    // The mode message goes clockwise around the finder, from the top left, and skips the reference grid of a full-range symbol
    private static void DrawModeMessage(bool[,] matrix, bool compact, int size, AztecBitList modeMessage)
    {
        var center = size / 2;
        if (compact)
        {
            for (var i = 0; i < 7; i++)
            {
                var offset = center - 3 + i;
                if (modeMessage[i])
                {
                    Set(matrix, offset, center - 5);
                }

                if (modeMessage[i + 7])
                {
                    Set(matrix, center + 5, offset);
                }

                if (modeMessage[20 - i])
                {
                    Set(matrix, offset, center + 5);
                }

                if (modeMessage[27 - i])
                {
                    Set(matrix, center - 5, offset);
                }
            }
        }
        else
        {
            for (var i = 0; i < 10; i++)
            {
                var offset = center - 5 + i + (i / 5);
                if (modeMessage[i])
                {
                    Set(matrix, offset, center - 7);
                }

                if (modeMessage[i + 10])
                {
                    Set(matrix, center + 7, offset);
                }

                if (modeMessage[29 - i])
                {
                    Set(matrix, offset, center + 7);
                }

                if (modeMessage[39 - i])
                {
                    Set(matrix, center - 7, offset);
                }
            }
        }
    }

    // The concentric squares of the finder pattern, and the orientation marks at its corners: 3 modules at the top left,
    // 2 at the top right, 1 at the bottom right
    private static void DrawBullsEye(bool[,] matrix, int center, int size)
    {
        for (var i = 0; i < size; i += 2)
        {
            for (var j = center - i; j <= center + i; j++)
            {
                Set(matrix, j, center - i);
                Set(matrix, j, center + i);
                Set(matrix, center - i, j);
                Set(matrix, center + i, j);
            }
        }

        Set(matrix, center - size, center - size);
        Set(matrix, center - size + 1, center - size);
        Set(matrix, center - size, center - size + 1);
        Set(matrix, center + size, center - size);
        Set(matrix, center + size, center - size + 1);
        Set(matrix, center + size, center + size - 1);
    }

    private static void Set(bool[,] matrix, int x, int y) => matrix[y, x] = true;
}
