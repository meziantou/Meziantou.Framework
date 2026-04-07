namespace Meziantou.Framework.Internal;

internal static class ErrorCorrectionEncoder
{
    /// <summary>
    /// Splits data into blocks, computes EC codewords, and interleaves everything.
    /// </summary>
    public static byte[] AddErrorCorrection(byte[] dataCodewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var ecCodewordsPerBlock = QRCodeVersion.GetECCodewordsPerBlock(version, ecLevel);
        var (group1Blocks, group1DataCW, group2Blocks, group2DataCW) = QRCodeVersion.GetBlockInfo(version, ecLevel);

        var totalBlocks = group1Blocks + group2Blocks;
        var dataBlocks = new byte[totalBlocks][];
        var ecBlocks = new byte[totalBlocks][];
        var generator = GaloisField.GenerateGeneratorPolynomial(ecCodewordsPerBlock);

        var dataIndex = 0;

        // Split data into blocks
        for (var i = 0; i < group1Blocks; i++)
        {
            dataBlocks[i] = new byte[group1DataCW];
            Array.Copy(dataCodewords, dataIndex, dataBlocks[i], 0, group1DataCW);
            dataIndex += group1DataCW;
        }

        for (var i = 0; i < group2Blocks; i++)
        {
            dataBlocks[group1Blocks + i] = new byte[group2DataCW];
            Array.Copy(dataCodewords, dataIndex, dataBlocks[group1Blocks + i], 0, group2DataCW);
            dataIndex += group2DataCW;
        }

        // Compute EC for each block
        for (var i = 0; i < totalBlocks; i++)
        {
            ecBlocks[i] = GaloisField.ComputeRemainder(dataBlocks[i], generator);
        }

        // Interleave data codewords
        var totalCodewords = QRCodeVersion.GetTotalCodewords(version);
        var result = new byte[totalCodewords];
        var resultIndex = 0;

        var maxDataCW = Math.Max(group1DataCW, group2DataCW);
        for (var col = 0; col < maxDataCW; col++)
        {
            for (var block = 0; block < totalBlocks; block++)
            {
                if (col < dataBlocks[block].Length)
                {
                    result[resultIndex++] = dataBlocks[block][col];
                }
            }
        }

        // Interleave EC codewords
        for (var col = 0; col < ecCodewordsPerBlock; col++)
        {
            for (var block = 0; block < totalBlocks; block++)
            {
                result[resultIndex++] = ecBlocks[block][col];
            }
        }

        return result;
    }
}
