using System.Globalization;
using Meziantou.Framework.Barcodes.Internal.MicroQR;
using Meziantou.Framework.Barcodes.Internal.RMQR;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Checks that a QR code can lose the modules cleared under its logo: the area must not cover what locates and describes
/// the symbol, and the error correction of each block must recover every codeword that has a module in the area.
/// </summary>
/// <remarks>
/// The logo can make any module of the area read as dark or light, so a codeword with a module in the area counts as an
/// error, whatever its value. The scanner does not know where the errors are, so a block of <c>n</c> error correction
/// codewords recovers <c>(n - p) / 2</c> codewords, where <c>p</c> is the number of misdecode protection codewords of the
/// smallest symbols (ISO/IEC 18004, Table 9).
/// </remarks>
internal static class QRLogoClearing
{
    /// <exception cref="ArgumentOutOfRangeException">The QR code cannot lose the modules of the area.</exception>
    public static void Validate(QRCode qrCode, ModuleRectangle area, string parameterName)
    {
        var structural = GetStructuralModules(qrCode);
        for (var row = area.Row; row < area.EndRow; row++)
        {
            for (var column = area.Column; column < area.EndColumn; column++)
            {
                if (structural[row, column])
                    throw new ArgumentOutOfRangeException(parameterName, $"The area cleared under the logo ({FormatArea(area)}) covers the module ({column.ToString(CultureInfo.InvariantCulture)}, {row.ToString(CultureInfo.InvariantCulture)}) of a finder pattern, a timing pattern, the format information or the version information: use a smaller logo or a smaller margin.");
            }
        }

        var (blockOfCodeword, blockCount, correctableCodewords) = GetBlocks(qrCode);
        var touched = GetTouchedCodewords(qrCode, area);
        var counts = new int[blockCount];
        for (var i = 0; i < touched.Length; i++)
        {
            if (touched[i])
            {
                counts[blockOfCodeword[i]]++;
            }
        }

        var worst = counts.Max();
        if (worst > correctableCodewords)
            throw new ArgumentOutOfRangeException(parameterName, $"The area cleared under the logo ({FormatArea(area)}) has modules of {worst.ToString(CultureInfo.InvariantCulture)} codewords of an error correction block, but level {qrCode.ErrorCorrectionLevel} of this QR code recovers at most {correctableCodewords.ToString(CultureInfo.InvariantCulture)} codewords per block: use ErrorCorrectionLevel.H, a smaller logo or a smaller margin.");
    }

    private static string FormatArea(ModuleRectangle area) => string.Create(CultureInfo.InvariantCulture, $"{area.ColumnCount}x{area.RowCount} modules at ({area.Column}, {area.Row})");

    /// <summary>Gets the modules of the function patterns and of the format and version information, but not the alignment patterns of standard QR codes.</summary>
    private static bool[,] GetStructuralModules(QRCode qrCode)
    {
        switch (qrCode.Type)
        {
            case QRCodeType.MicroQR:
                var microBuilder = new MicroQRMatrixBuilder(qrCode.Version);
                microBuilder.PlaceFunctionPatternsAndData(new byte[MicroQRVersion.GetTotalCodewords(qrCode.Version)], qrCode.ErrorCorrectionLevel);
                return microBuilder.IsReserved;

            case QRCodeType.RMQR:
                // The alignment patterns of rMQR codes are joined by vertical timing patterns, which the scanner needs
                return new RMQRMatrixBuilder(qrCode.Version).GetReservedModules();

            default:
                var builder = new MatrixBuilder(qrCode.Version);
                builder.PlaceFunctionPatternsAndData(new byte[QRCodeVersion.GetTotalCodewords(qrCode.Version)]);
                var structural = (bool[,])builder.IsReserved.Clone();

                // The alignment patterns correct the distortion of large symbols, but scanners read the symbol without the
                // ones under a logo: those away from the finder patterns are not structural
                var positions = QRCodeVersion.GetAlignmentPatternPositions(qrCode.Version);
                for (var i = 0; i < positions.Length; i++)
                {
                    for (var j = 0; j < positions.Length; j++)
                    {
                        var isFinderCorner = (i == 0 && j == 0) || (i == 0 && j == positions.Length - 1) || (i == positions.Length - 1 && j == 0);
                        if (isFinderCorner)
                            continue;

                        for (var row = positions[i] - 2; row <= positions[i] + 2; row++)
                        {
                            for (var column = positions[j] - 2; column <= positions[j] + 2; column++)
                            {
                                structural[row, column] = false;
                            }
                        }
                    }
                }

                return structural;
        }
    }

    /// <summary>Gets, for each codeword in the order of the symbol, whether a data module of the area belongs to it.</summary>
    private static bool[] GetTouchedCodewords(QRCode qrCode, ModuleRectangle area)
    {
        // The codewords read from the area alone differ from those read from an empty symbol exactly where the area has a module
        var empty = new bool[qrCode.Height, qrCode.Width];
        var probe = new bool[qrCode.Height, qrCode.Width];
        for (var row = area.Row; row < area.EndRow; row++)
        {
            for (var column = area.Column; column < area.EndColumn; column++)
            {
                probe[row, column] = true;
            }
        }

        var baseline = ReadCodewords(qrCode, empty);
        var codewords = ReadCodewords(qrCode, probe);
        var touched = new bool[codewords.Length];
        for (var i = 0; i < codewords.Length; i++)
        {
            touched[i] = codewords[i] != baseline[i];
        }

        return touched;
    }

    private static byte[] ReadCodewords(QRCode qrCode, bool[,] modules)
    {
        switch (qrCode.Type)
        {
            case QRCodeType.MicroQR:
                var microBuilder = new MicroQRMatrixBuilder(qrCode.Version);
                microBuilder.PlaceFunctionPatternsAndData(new byte[MicroQRVersion.GetTotalCodewords(qrCode.Version)], qrCode.ErrorCorrectionLevel);
                Array.Copy(modules, microBuilder.Modules, modules.Length);
                return microBuilder.ReadDataBits(qrCode.ErrorCorrectionLevel);

            case QRCodeType.RMQR:
                return new RMQRMatrixBuilder(qrCode.Version).ReadDataBits(modules);

            default:
                var builder = new MatrixBuilder(qrCode.Version);
                var totalCodewords = QRCodeVersion.GetTotalCodewords(qrCode.Version);
                builder.PlaceFunctionPatternsAndData(new byte[totalCodewords]);
                Array.Copy(modules, builder.Modules, modules.Length);
                return builder.ReadDataBits(totalCodewords);
        }
    }

    /// <summary>Gets the block of each codeword in the order of the symbol (ISO/IEC 18004, 7.6), and the number of codewords a block recovers.</summary>
    private static (int[] BlockOfCodeword, int BlockCount, int CorrectableCodewords) GetBlocks(QRCode qrCode)
    {
        var version = qrCode.Version;
        var level = qrCode.ErrorCorrectionLevel;
        int group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords, ecCodewordsPerBlock, misdecodeProtection;
        switch (qrCode.Type)
        {
            case QRCodeType.MicroQR:
                (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords) = (1, MicroQRVersion.GetDataCodewords(version, level), 0, 0);
                ecCodewordsPerBlock = MicroQRVersion.GetECCodewords(version, level);

                // M1 detects errors but corrects none
                misdecodeProtection = (version, level) switch
                {
                    (1, _) => ecCodewordsPerBlock,
                    (2, ErrorCorrectionLevel.L) => 3,
                    (2, _) or (3, ErrorCorrectionLevel.L) or (4, ErrorCorrectionLevel.L) => 2,
                    _ => 0,
                };
                break;

            case QRCodeType.RMQR:
                (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords, ecCodewordsPerBlock) = RMQRVersion.GetErrorCorrectionBlocks(version, level);
                misdecodeProtection = 0;
                break;

            default:
                (group1Blocks, group1DataCodewords, group2Blocks, group2DataCodewords) = QRCodeVersion.GetBlockInfo(version, level);
                ecCodewordsPerBlock = QRCodeVersion.GetECCodewordsPerBlock(version, level);
                misdecodeProtection = (version, level) switch
                {
                    (1, ErrorCorrectionLevel.L) => 3,
                    (1, ErrorCorrectionLevel.M) or (2, ErrorCorrectionLevel.L) => 2,
                    (1, _) or (3, ErrorCorrectionLevel.L) => 1,
                    _ => 0,
                };
                break;
        }

        // The data codewords of the blocks are interleaved, then their error correction codewords (as QRCodeDecoder reads them)
        var blockCount = group1Blocks + group2Blocks;
        var blockOfCodeword = new int[(group1Blocks * group1DataCodewords) + (group2Blocks * group2DataCodewords) + (blockCount * ecCodewordsPerBlock)];
        var index = 0;
        for (var column = 0; column < Math.Max(group1DataCodewords, group2DataCodewords); column++)
        {
            for (var block = 0; block < blockCount; block++)
            {
                if (column < (block < group1Blocks ? group1DataCodewords : group2DataCodewords))
                {
                    blockOfCodeword[index++] = block;
                }
            }
        }

        for (var column = 0; column < ecCodewordsPerBlock; column++)
        {
            for (var block = 0; block < blockCount; block++)
            {
                blockOfCodeword[index++] = block;
            }
        }

        return (blockOfCodeword, blockCount, (ecCodewordsPerBlock - misdecodeProtection) / 2);
    }
}
