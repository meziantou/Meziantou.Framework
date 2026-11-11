namespace Meziantou.Framework.Barcodes.Internal.RMQR;

internal static class RMQREncoder
{
    public static QRCode Encode(string data, ErrorCorrectionLevel ecLevel, RMQRSizeConstraints constraints)
    {
        var allowSegments = DataAnalyzer.CanSegmentWithoutEci(data);
        QRSegment[]? bestSegments = null;
        var bestVersion = -1;
        var bestArea = int.MaxValue;
        for (var version = 1; version <= 32; version++)
        {
            var area = RMQRVersion.GetHeight(version) * RMQRVersion.GetWidth(version);
            if (area >= bestArea || !constraints.Allows(RMQRVersion.GetHeight(version), RMQRVersion.GetWidth(version)))
                continue;

            var segments = QRSegmentOptimizer.ChooseSegments(data, QRCodeType.RMQR, version, allowSegments, allowKanji: true, out var bitCount);
            if (segments is not null && bitCount >= 0 && bitCount <= RMQRVersion.GetDataCodewords(version, ecLevel) * 8)
            {
                bestSegments = segments;
                bestVersion = version;
                bestArea = area;
            }
        }

        if (bestSegments is null)
            throw CreateDataTooLongException(constraints);

        return Build(QRSegmentOptimizer.ToSegments(data, bestSegments), bestVersion, ecLevel);
    }

    public static QRCode Encode(IReadOnlyList<QRCodeSegment> segments, ErrorCorrectionLevel ecLevel, RMQRSizeConstraints constraints)
    {
        var bestVersion = -1;
        var bestArea = int.MaxValue;
        for (var version = 1; version <= 32; version++)
        {
            var area = RMQRVersion.GetHeight(version) * RMQRVersion.GetWidth(version);
            if (area >= bestArea || !constraints.Allows(RMQRVersion.GetHeight(version), RMQRVersion.GetWidth(version)))
                continue;

            var bitCount = QRSymbolFormat.GetBitCount(segments, QRCodeType.RMQR, version);
            if (bitCount >= 0 && bitCount <= RMQRVersion.GetDataCodewords(version, ecLevel) * 8)
            {
                bestVersion = version;
                bestArea = area;
            }
        }

        if (bestVersion < 0)
            throw CreateDataTooLongException(constraints);

        return Build(segments, bestVersion, ecLevel);
    }

    private static InvalidOperationException CreateDataTooLongException(RMQRSizeConstraints constraints)
    {
        return new InvalidOperationException(constraints.IsEmpty ? "The data is too long to be encoded in an rMQR code." : $"The data is too long to be encoded in an rMQR code of the allowed sizes ({constraints}).");
    }

    private static QRCode Build(IReadOnlyList<QRCodeSegment> segments, int version, ErrorCorrectionLevel ecLevel)
    {
        var buffer = new BitBuffer();
        QRSymbolFormat.Write(buffer, segments, QRCodeType.RMQR, version);
        DataEncoderHelper.AddTerminatorAndPadding(buffer, RMQRVersion.GetDataCodewords(version, ecLevel) * 8, RMQRVersion.GetTerminatorBits());

        var allCodewords = AddErrorCorrection(buffer.ToByteArray(), version, ecLevel);
        var builder = new RMQRMatrixBuilder(version);
        builder.Build(allCodewords, ecLevel);

        return new QRCode(builder.Modules, version, QRCodeType.RMQR, ecLevel);
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
