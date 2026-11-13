using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.Barcodes.Internal.MicroQR;
using Meziantou.Framework.Barcodes.Internal.RMQR;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Reads the data of QR codes, Micro QR codes and rMQR codes from their modules: the format information, the error
/// correction, and the segments of the data.
/// </summary>
/// <remarks>
/// The decoder works on modules, not on pixels. To read the QR codes of an image, use the <c>ReadQRCodes</c> methods of
/// the <c>Meziantou.Framework.Imaging.Barcodes</c> package, which locate the symbols and sample their modules.
/// </remarks>
public static class QRCodeDecoder
{
    /// <summary>Reads the data of a QR code.</summary>
    /// <param name="qrCode">The QR code.</param>
    /// <returns>The content of the QR code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs" ref:region="QRCodeDecoder_Decode" ref:dedent="true">
    /// var qrCode = QRCode.Create("https://www.meziantou.net", ErrorCorrectionLevel.M);
    ///
    /// var result = QRCodeDecoder.Decode(qrCode);
    ///
    /// Assert.Equal("https://www.meziantou.net", result.Text);
    /// Assert.Equal((QRCodeType.Standard, 2, ErrorCorrectionLevel.M), (result.Type, result.Version, result.ErrorCorrectionLevel));
    /// </code>
    /// </example>
    public static QRCodeDecodeResult Decode(QRCode qrCode)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        return Decode(qrCode.Modules);
    }

    /// <summary>Reads the data of a QR code from its modules.</summary>
    /// <param name="modules">
    /// The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone. The size of the matrix
    /// gives the type of the symbol: a square of 21 to 177 modules is a standard QR code, a square of 11 to 17 modules a
    /// Micro QR code, and a rectangle an rMQR code.
    /// </param>
    /// <returns>The content of the QR code.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The modules are not a QR code, or have more errors than the error correction can fix.</exception>
    public static QRCodeDecodeResult Decode(bool[,] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (TryDecode(modules, out var result, out var error))
            return result;

        throw new FormatException(error);
    }

    /// <summary>Tries to read the data of a QR code from its modules.</summary>
    /// <param name="modules">The modules, <see langword="true"/> for dark, indexed by row then column, without quiet zone.</param>
    /// <param name="result">When this method returns <see langword="true"/>, the content of the QR code.</param>
    /// <returns><see langword="true"/> if the modules are a QR code that could be read; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    public static bool TryDecode(bool[,] modules, [NotNullWhen(true)] out QRCodeDecodeResult? result)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return TryDecode(modules, out result, out _);
    }

    /// <summary>Puts the data of a structured append sequence back together.</summary>
    /// <param name="results">The decoded symbols of the sequence, in any order.</param>
    /// <returns>The text of the sequence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/> or contains <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A symbol is not part of a structured append sequence, the symbols belong to different sequences, a symbol is
    /// missing or repeated, or the parity of the data does not match.
    /// </exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Barcodes.Tests/QRCodeTests.cs" ref:region="QRCodeDecoder_MergeStructuredAppend" ref:dedent="true">
    /// var text = "Structured append splits this text across several QR codes, and the reader puts it back together.";
    /// var symbols = QRCode.CreateStructuredAppend(text, ErrorCorrectionLevel.M, new QRCodeOptions { MaximumVersion = 2 });
    ///
    /// // The symbols can be read in any order
    /// var results = symbols.Reverse().Select(QRCodeDecoder.Decode).ToList();
    ///
    /// Assert.Equal(text, QRCodeDecoder.MergeStructuredAppend(results));
    /// </code>
    /// </example>
    public static string MergeStructuredAppend(IEnumerable<QRCodeDecodeResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        QRCodeDecodeResult[]? parts = null;
        byte parity = 0;
        foreach (var result in results)
        {
            if (result is null)
                throw new ArgumentNullException(nameof(results), "The sequence contains a null result.");

            if (result.StructuredAppend is not { } structuredAppend)
                throw new ArgumentException("A symbol is not part of a structured append sequence.", nameof(results));

            if (parts is null)
            {
                parts = new QRCodeDecodeResult[structuredAppend.Count];
                parity = structuredAppend.Parity;
            }
            else if (parts.Length != structuredAppend.Count || parity != structuredAppend.Parity)
            {
                throw new ArgumentException("The symbols belong to different structured append sequences: their number of symbols or their parity differ.", nameof(results));
            }

            if (parts[structuredAppend.Index] is not null)
                throw new ArgumentException($"The symbol {structuredAppend} is repeated.", nameof(results));

            parts[structuredAppend.Index] = result;
        }

        if (parts is null)
            throw new ArgumentException("The sequence is empty.", nameof(results));

        var missing = Array.IndexOf(parts, null);
        if (missing >= 0)
            throw new ArgumentException($"The symbol {missing + 1}/{parts.Length} is missing.", nameof(results));

        var segments = parts.SelectMany(part => part.Segments).ToArray();
        var actualParity = 0;
        foreach (var segment in segments)
        {
            actualParity ^= segment.GetParity();
        }

        if (actualParity != parity)
            throw new ArgumentException("The parity of the data does not match the parity of the sequence.", nameof(results));

        return EciCharacterSets.GetText(segments);
    }

    private static bool TryDecode(bool[,] modules, [NotNullWhen(true)] out QRCodeDecodeResult? result, out string error)
    {
        var failure = TryDecodeOriented(modules, isMirrored: false, out result);
        if (result is not null)
        {
            error = "";
            return true;
        }

        error = failure ?? "";
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        if (height != width)
            return false;

        // A mirrored symbol, once its finder patterns are in place, is the transpose of the symbol
        var transposed = new bool[width, height];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                transposed[column, row] = modules[row, column];
            }
        }

        TryDecodeOriented(transposed, isMirrored: true, out result);
        return result is not null;
    }

    // Returns null on success, or the reason of the failure
    private static string? TryDecodeOriented(bool[,] modules, bool isMirrored, out QRCodeDecodeResult? result)
    {
        result = null;
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        if (height != width)
            return TryDecodeRMQR(modules, isMirrored, out result);

        if (height is 11 or 13 or 15 or 17)
            return TryDecodeMicroQR(modules, isMirrored, out result);

        if (height is >= 21 and <= 177 && (height - 17) % 4 is 0)
            return TryDecodeStandard(modules, isMirrored, out result);

        return $"A matrix of {height}x{width} modules is not the size of a QR code.";
    }

    private static string? TryDecodeStandard(bool[,] modules, bool isMirrored, out QRCodeDecodeResult? result)
    {
        result = null;
        var size = modules.GetLength(0);
        var version = (size - 17) / 4;

        // The two copies of the format information (ISO/IEC 18004, 7.9.1)
        ReadOnlySpan<int> horizontalColumns = [0, 1, 2, 3, 4, 5, 7, 8];
        ReadOnlySpan<int> verticalRows = [7, 5, 4, 3, 2, 1, 0];
        var copy1 = 0;
        var copy2 = 0;
        for (var i = 0; i < 8; i++)
        {
            copy1 = (copy1 << 1) | (modules[8, horizontalColumns[i]] ? 1 : 0);
        }

        for (var i = 0; i < 7; i++)
        {
            copy1 = (copy1 << 1) | (modules[verticalRows[i], 8] ? 1 : 0);
        }

        for (var i = 0; i < 7; i++)
        {
            copy2 = (copy2 << 1) | (modules[size - 1 - i, 8] ? 1 : 0);
        }

        for (var i = 7; i >= 0; i--)
        {
            copy2 = (copy2 << 1) | (modules[8, size - 1 - i] ? 1 : 0);
        }

        if (!FormatInfo.TryDecodeFormatInfo(copy1, copy2, out var ecLevel, out var maskPattern))
            return "The format information of the QR code cannot be read.";

        var builder = new MatrixBuilder(version);
        builder.PlaceFunctionPatternsAndData(new byte[QRCodeVersion.GetTotalCodewords(version)]);
        Array.Copy(modules, builder.Modules, modules.Length);
        builder.ApplyMask(maskPattern);
        var codewords = builder.ReadDataBits(QRCodeVersion.GetTotalCodewords(version));

        var (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords) = QRCodeVersion.GetBlockInfo(version, ecLevel);
        var correctedErrorCount = CorrectBlocks(codewords, group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords, QRCodeVersion.GetECCodewordsPerBlock(version, ecLevel), out var data);
        if (correctedErrorCount < 0)
            return "The QR code has more errors than the error correction can fix.";

        return CreateResult(QRCodeType.Standard, version, ecLevel, maskPattern, data, data.Length * 8, correctedErrorCount, isMirrored, out result);
    }

    private static string? TryDecodeMicroQR(bool[,] modules, bool isMirrored, out QRCodeDecodeResult? result)
    {
        result = null;
        var size = modules.GetLength(0);
        var version = (size - 9) / 2;

        // ISO/IEC 18004, 7.9.2: bits 14 to 7 along row 8, then bits 6 to 0 up column 8
        var formatBits = 0;
        for (var i = 0; i < 8; i++)
        {
            formatBits = (formatBits << 1) | (modules[8, 1 + i] ? 1 : 0);
        }

        for (var i = 0; i < 7; i++)
        {
            formatBits = (formatBits << 1) | (modules[7 - i, 8] ? 1 : 0);
        }

        if (!MicroQRFormatInfo.TryDecode(formatBits, version, out var ecLevel, out var maskPattern))
            return "The format information of the Micro QR code cannot be read.";

        var builder = new MicroQRMatrixBuilder(version);
        builder.PlaceFunctionPatternsAndData(new byte[MicroQRVersion.GetTotalCodewords(version)], ecLevel);
        Array.Copy(modules, builder.Modules, modules.Length);
        builder.ApplyMask(maskPattern);
        var codewords = builder.ReadDataBits(ecLevel);

        var dataCodewords = MicroQRVersion.GetDataCodewords(version, ecLevel);
        var correctedErrorCount = CorrectBlocks(codewords, 1, dataCodewords, 0, 0, MicroQRVersion.GetECCodewords(version, ecLevel), out var data);

        // M1 detects errors but does not correct them (ISO/IEC 18004, Table 9)
        if (correctedErrorCount < 0 || (version is 1 && correctedErrorCount > 0))
            return "The Micro QR code has more errors than the error correction can fix.";

        return CreateResult(QRCodeType.MicroQR, version, ecLevel, maskPattern, data, MicroQRVersion.GetDataBitCapacity(version, ecLevel), correctedErrorCount, isMirrored, out result);
    }

    private static string? TryDecodeRMQR(bool[,] modules, bool isMirrored, out QRCodeDecodeResult? result)
    {
        result = null;
        var height = modules.GetLength(0);
        var width = modules.GetLength(1);
        var version = 0;
        for (var candidate = 1; candidate <= 32; candidate++)
        {
            if (RMQRVersion.GetHeight(candidate) == height && RMQRVersion.GetWidth(candidate) == width)
            {
                version = candidate;
                break;
            }
        }

        if (version is 0)
            return $"A matrix of {height}x{width} modules is not the size of a QR code.";

        var builder = new RMQRMatrixBuilder(version);
        var finderSide = 0;
        foreach (var (row, column) in RMQRMatrixBuilder.GetFinderSideFormatCoordinates())
        {
            finderSide = (finderSide << 1) | (modules[row, column] ? 1 : 0);
        }

        var subFinderSide = 0;
        foreach (var (row, column) in builder.GetSubFinderSideFormatCoordinates())
        {
            subFinderSide = (subFinderSide << 1) | (modules[row, column] ? 1 : 0);
        }

        if (!RMQRFormatInfo.TryDecode(finderSide, subFinderSide, out var ecLevel, out var formatVersion) || formatVersion != version)
            return "The format information of the rMQR code cannot be read.";

        var codewords = builder.ReadDataBits(modules);
        var (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords, ecCodewordsPerBlock) = RMQRVersion.GetErrorCorrectionBlocks(version, ecLevel);
        var correctedErrorCount = CorrectBlocks(codewords, group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords, ecCodewordsPerBlock, out var data);
        if (correctedErrorCount < 0)
            return "The rMQR code has more errors than the error correction can fix.";

        return CreateResult(QRCodeType.RMQR, version, ecLevel, maskPattern: null, data, data.Length * 8, correctedErrorCount, isMirrored, out result);
    }

    private static string? CreateResult(QRCodeType type, int version, ErrorCorrectionLevel ecLevel, int? maskPattern, byte[] data, int dataBitCount, int correctedErrorCount, bool isMirrored, out QRCodeDecodeResult? result)
    {
        result = null;
        var segments = new List<QRCodeSegment>();
        var error = QRBitstreamReader.TryRead(data, dataBitCount, type, version, segments, out var structuredAppend);
        if (error is not null)
            return error;

        result = new QRCodeDecodeResult(type, version, ecLevel, maskPattern, structuredAppend, segments, EciCharacterSets.GetText(segments), correctedErrorCount, isMirrored);
        return null;
    }

    /// <summary>
    /// Splits interleaved codewords into their blocks (ISO/IEC 18004, 7.6), corrects each block, and returns the number of
    /// corrected codewords, or -1 when a block cannot be corrected.
    /// </summary>
    private static int CorrectBlocks(byte[] codewords, int group1Blocks, int group1DataCodewords, int group2Blocks, int group2DataCodewords, int ecCodewordsPerBlock, out byte[] data)
    {
        var blockCount = group1Blocks + group2Blocks;
        var blocks = new int[blockCount][];
        for (var block = 0; block < blockCount; block++)
        {
            blocks[block] = new int[GetDataLength(block) + ecCodewordsPerBlock];
        }

        var index = 0;
        var maxDataCodewords = Math.Max(group1DataCodewords, group2DataCodewords);
        for (var column = 0; column < maxDataCodewords; column++)
        {
            for (var block = 0; block < blockCount; block++)
            {
                if (column < GetDataLength(block))
                {
                    blocks[block][column] = codewords[index++];
                }
            }
        }

        for (var column = 0; column < ecCodewordsPerBlock; column++)
        {
            for (var block = 0; block < blockCount; block++)
            {
                blocks[block][GetDataLength(block) + column] = codewords[index++];
            }
        }

        var correctedErrorCount = 0;
        data = new byte[(group1Blocks * group1DataCodewords) + (group2Blocks * group2DataCodewords)];
        var dataIndex = 0;
        for (var block = 0; block < blockCount; block++)
        {
            var corrected = ReedSolomonDecoder.Correct(BinaryGaloisField.QRCode256, blocks[block], ecCodewordsPerBlock, firstRoot: 0);
            if (corrected < 0)
                return -1;

            correctedErrorCount += corrected;
            for (var i = 0; i < GetDataLength(block); i++)
            {
                data[dataIndex++] = (byte)blocks[block][i];
            }
        }

        return correctedErrorCount;

        int GetDataLength(int block) => block < group1Blocks ? group1DataCodewords : group2DataCodewords;
    }
}
