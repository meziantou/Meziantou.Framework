using System.Text;

namespace Meziantou.Framework.Internal.RMQR;

internal static class RMQREncoder
{
    public static QRCode Encode(string data, ErrorCorrectionLevel ecLevel)
    {
        if (ecLevel is not (ErrorCorrectionLevel.M or ErrorCorrectionLevel.H))
        {
            throw new ArgumentOutOfRangeException(nameof(ecLevel), $"rMQR only supports EC levels M and H, got {ecLevel}.");
        }

        var mode = DataAnalyzer.DetermineMode(data);
        var version = RMQRVersion.DetermineVersion(data, ecLevel, mode);

        var dataCodewords = EncodeData(data, version, ecLevel, mode);
        var allCodewords = AddErrorCorrection(dataCodewords, version, ecLevel);

        var builder = new RMQRMatrixBuilder(version);
        builder.Build(allCodewords, ecLevel);

        return new QRCode(builder.Modules, version, QRCodeType.RMQR);
    }

    private static byte[] EncodeData(string data, int version, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var buffer = new BitBuffer();

        // Mode indicator (3 bits)
        var modeValue = RMQRVersion.GetModeIndicatorValue(mode);
        buffer.Append(modeValue, RMQRVersion.GetModeIndicatorBits());

        // Character count indicator
        var cciBits = RMQRVersion.GetCharacterCountBits(version, mode);
        var charCount = mode switch
        {
            EncodingMode.Byte => Encoding.UTF8.GetByteCount(data),
            EncodingMode.Kanji => data.Length,
            _ => data.Length,
        };
        buffer.Append(charCount, cciBits);

        // Data encoding using shared helper
        DataEncoderHelper.EncodeData(buffer, data, mode);

        // Add terminator and padding
        var totalDataBits = RMQRVersion.GetDataCodewords(version, ecLevel) * 8;
        var terminatorMaxBits = RMQRVersion.GetTerminatorBits();
        DataEncoderHelper.AddTerminatorAndPadding(buffer, totalDataBits, terminatorMaxBits);

        return buffer.ToByteArray();
    }

    private static byte[] AddErrorCorrection(byte[] dataCodewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var (group1BlockCount, group1DataCodewords, group2BlockCount, group2DataCodewords, ecCodewordsPerBlock) =
            RMQRVersion.GetErrorCorrectionBlocks(version, ecLevel);
        var totalBlockCount = group1BlockCount + group2BlockCount;
        var totalDataCodewords = RMQRVersion.GetDataCodewords(version, ecLevel);
        var totalCodewords = RMQRVersion.GetTotalCodewords(version);
        var generatorPolynomial = GaloisField.GenerateGeneratorPolynomial(ecCodewordsPerBlock);

        var dataBlocks = new byte[totalBlockCount][];
        var ecBlocks = new byte[totalBlockCount][];

        var dataOffset = 0;
        var blockIndex = 0;
        for (var i = 0; i < group1BlockCount; i++)
        {
            var blockData = new byte[group1DataCodewords];
            Array.Copy(dataCodewords, dataOffset, blockData, 0, group1DataCodewords);
            dataBlocks[blockIndex] = blockData;
            ecBlocks[blockIndex] = GaloisField.ComputeRemainder(blockData, generatorPolynomial);
            dataOffset += group1DataCodewords;
            blockIndex++;
        }

        for (var i = 0; i < group2BlockCount; i++)
        {
            var blockData = new byte[group2DataCodewords];
            Array.Copy(dataCodewords, dataOffset, blockData, 0, group2DataCodewords);
            dataBlocks[blockIndex] = blockData;
            ecBlocks[blockIndex] = GaloisField.ComputeRemainder(blockData, generatorPolynomial);
            dataOffset += group2DataCodewords;
            blockIndex++;
        }

        if (dataOffset != totalDataCodewords)
        {
            throw new InvalidOperationException("rMQR data codeword distribution mismatch.");
        }

        var result = new byte[totalCodewords];
        var resultOffset = 0;

        var maxDataCodewordsPerBlock = Math.Max(group1DataCodewords, group2DataCodewords);
        for (var i = 0; i < maxDataCodewordsPerBlock; i++)
        {
            for (var j = 0; j < totalBlockCount; j++)
            {
                var block = dataBlocks[j];
                if (i < block.Length)
                {
                    result[resultOffset++] = block[i];
                }
            }
        }

        for (var i = 0; i < ecCodewordsPerBlock; i++)
        {
            for (var j = 0; j < totalBlockCount; j++)
            {
                result[resultOffset++] = ecBlocks[j][i];
            }
        }

        if (resultOffset != totalCodewords)
        {
            throw new InvalidOperationException("rMQR interleaving produced an unexpected number of codewords.");
        }

        return result;
    }
}
