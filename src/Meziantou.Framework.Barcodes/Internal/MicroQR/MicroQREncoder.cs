namespace Meziantou.Framework.Barcodes.Internal.MicroQR;

internal static class MicroQREncoder
{
    public static QRCode Encode(string data, ErrorCorrectionLevel ecLevel)
    {
        var mode = DataAnalyzer.DetermineMode(data);
        var version = MicroQRVersion.DetermineVersion(data, ecLevel, mode);
        var effectiveECLevel = MicroQRVersion.ResolveECLevel(version, ecLevel);

        var dataCodewords = EncodeData(data, version, effectiveECLevel, mode);
        var allCodewords = AddErrorCorrection(dataCodewords, version, effectiveECLevel);

        var bestMask = FindBestMask(allCodewords, version, effectiveECLevel);

        var builder = new MicroQRMatrixBuilder(version);
        builder.Build(allCodewords, effectiveECLevel, bestMask);

        return new QRCode(builder.Modules, version, QRCodeType.MicroQR, effectiveECLevel);
    }

    private static byte[] EncodeData(string data, int version, ErrorCorrectionLevel ecLevel, EncodingMode mode)
    {
        var buffer = new BitBuffer();

        // Mode indicator
        var modeIndicatorBits = MicroQRVersion.GetModeIndicatorBits(version);
        if (modeIndicatorBits > 0)
        {
            var modeValue = MicroQRVersion.GetModeIndicatorValue(version, mode);
            buffer.Append(modeValue, modeIndicatorBits);
        }

        // Character count indicator
        var cciBits = MicroQRVersion.GetCharacterCountBits(version, mode);
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
        var dataCodewords = MicroQRVersion.GetDataCodewords(version, ecLevel);
        var terminatorMaxBits = MicroQRVersion.GetTerminatorBits(version);
        if (version % 2 == 0)
        {
            DataEncoderHelper.AddTerminatorAndPadding(buffer, dataCodewords * 8, terminatorMaxBits);
            return buffer.ToByteArray();
        }

        // In M1 and M3 the last data codeword has 4 bits (ISO/IEC 18004, 7.4.10). It is stored in the high nibble of the
        // last byte, the low nibble being 0, which is also how the error correction sees it. The pad codewords fill the
        // full codewords only: the 4-bit codeword is padded with 0000.
        var fullCodewordBits = (dataCodewords - 1) * 8;
        var capacityBits = fullCodewordBits + 4;
        if (buffer.BitCount > capacityBits)
            throw new InvalidOperationException("The data is too long to be encoded in a Micro QR code.");

        buffer.Append(0, Math.Min(terminatorMaxBits, capacityBits - buffer.BitCount));
        if (buffer.BitCount <= fullCodewordBits)
        {
            DataEncoderHelper.AddTerminatorAndPadding(buffer, fullCodewordBits, terminatorMaxBits: 0);
        }

        buffer.Append(0, (dataCodewords * 8) - buffer.BitCount);
        return buffer.ToByteArray();
    }

    private static byte[] AddErrorCorrection(byte[] dataCodewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var dataCWCount = MicroQRVersion.GetDataCodewords(version, ecLevel);
        var ecCWCount = MicroQRVersion.GetECCodewords(version, ecLevel);
        var totalCWCount = MicroQRVersion.GetTotalCodewords(version);

        // Micro QR uses a single block (no interleaving needed)
        var dataBlock = new byte[dataCWCount];
        Array.Copy(dataCodewords, dataBlock, dataCWCount);

        var generator = GaloisField.GenerateGeneratorPolynomial(ecCWCount);
        var ecBlock = GaloisField.ComputeRemainder(dataBlock, generator);

        // Combine: data codewords followed by EC codewords
        var result = new byte[totalCWCount];
        Array.Copy(dataBlock, 0, result, 0, dataCWCount);
        Array.Copy(ecBlock, 0, result, dataCWCount, ecCWCount);

        return result;
    }

    /// <summary>
    /// Picks the mask with the highest score, per ISO/IEC 18004 section 7.8.3.2.
    /// </summary>
    /// <remarks>
    /// Micro QR maximises its mask score, unlike standard QR which minimises a penalty. Flipping
    /// this comparison to match <see cref="MaskEvaluator.FindBestMask"/> would silently degrade
    /// every Micro QR symbol.
    /// </remarks>
    private static int FindBestMask(byte[] codewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var bestMask = 0;
        var bestScore = -1;

        for (var mask = 0; mask < 4; mask++)
        {
            var builder = new MicroQRMatrixBuilder(version);
            builder.Build(codewords, ecLevel, mask);
            var score = MicroQRMatrixBuilder.EvaluateMaskScore(builder.Modules, builder.Size);

            if (score > bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        return bestMask;
    }
}
