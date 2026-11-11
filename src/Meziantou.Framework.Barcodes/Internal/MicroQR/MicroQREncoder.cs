using System.Globalization;

namespace Meziantou.Framework.Barcodes.Internal.MicroQR;

internal static class MicroQREncoder
{
    public static QRCode Encode(string data, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion)
    {
        var allowSegments = DataAnalyzer.CanSegmentWithoutEci(data);
        var isEncodable = false;
        for (var version = minVersion; version <= maxVersion; version++)
        {
            if (!MicroQRVersion.IsECLevelSupported(version, ecLevel))
                continue;

            var segments = QRSegmentOptimizer.ChooseSegments(data, QRCodeType.MicroQR, version, allowSegments, allowKanji: true, out var bitCount);
            if (segments is null)
                continue;

            isEncodable = true;
            var effectiveECLevel = MicroQRVersion.ResolveECLevel(version, ecLevel);
            if (bitCount >= 0 && bitCount <= MicroQRVersion.GetDataBitCapacity(version, effectiveECLevel))
                return Build(QRSegmentOptimizer.ToSegments(data, segments), version, effectiveECLevel);
        }

        throw CreateException(isEncodable, ecLevel, minVersion, maxVersion, "The data has characters that a Micro QR code of version {0} cannot encode with the error correction level {1}: M1 encodes digits, M2 digits and uppercase alphanumeric characters.");
    }

    public static QRCode Encode(IReadOnlyList<QRCodeSegment> segments, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion)
    {
        var isEncodable = false;
        for (var version = minVersion; version <= maxVersion; version++)
        {
            if (!MicroQRVersion.IsECLevelSupported(version, ecLevel) || (version is 1 && segments.Count is not 1) || !segments.All(segment => QRSymbolFormat.GetCharacterCountBits(QRCodeType.MicroQR, version, segment.EncodingMode) >= 0))
                continue;

            isEncodable = true;
            var effectiveECLevel = MicroQRVersion.ResolveECLevel(version, ecLevel);
            var bitCount = QRSymbolFormat.GetBitCount(segments, QRCodeType.MicroQR, version);
            if (bitCount >= 0 && bitCount <= MicroQRVersion.GetDataBitCapacity(version, effectiveECLevel))
                return Build(segments, version, effectiveECLevel);
        }

        throw CreateException(isEncodable, ecLevel, minVersion, maxVersion, "The segments have a mode that a Micro QR code of version {0} cannot encode with the error correction level {1}: M1 holds a single numeric segment, M2 numeric and alphanumeric segments.");
    }

    private static InvalidOperationException CreateException(bool isEncodable, ErrorCorrectionLevel ecLevel, int minVersion, int maxVersion, [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string unsupportedMessage)
    {
        var range = minVersion == maxVersion ? $"M{maxVersion}" : $"M{minVersion} to M{maxVersion}";
        if (!isEncodable)
            return new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, unsupportedMessage, range, ecLevel));

        if (minVersion == 1 && maxVersion == 4)
            return new InvalidOperationException("The data is too long to be encoded in a Micro QR code.");

        return new InvalidOperationException($"The data is too long to be encoded in a Micro QR code of version {range}.");
    }

    private static QRCode Build(IReadOnlyList<QRCodeSegment> segments, int version, ErrorCorrectionLevel effectiveECLevel)
    {
        var dataCodewords = EncodeData(segments, version, effectiveECLevel);
        var allCodewords = AddErrorCorrection(dataCodewords, version, effectiveECLevel);

        var modules = BuildMatrix(allCodewords, version, effectiveECLevel);
        return new QRCode(modules, version, QRCodeType.MicroQR, effectiveECLevel);
    }

    private static byte[] EncodeData(IReadOnlyList<QRCodeSegment> segments, int version, ErrorCorrectionLevel ecLevel)
    {
        var buffer = new BitBuffer();
        QRSymbolFormat.Write(buffer, segments, QRCodeType.MicroQR, version);

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
    /// Builds the Micro QR matrix using the mask with the highest score, per ISO/IEC 18004 section 7.8.3.2.
    /// </summary>
    /// <remarks>
    /// Micro QR maximises its mask score, unlike standard QR which minimises a penalty. Flipping
    /// this comparison to match <see cref="MaskEvaluator.BuildMatrix"/> would silently degrade
    /// every Micro QR symbol.
    /// The function patterns and unmasked data are placed once; each candidate mask is applied in place, scored, then
    /// removed again, so a single matrix is allocated. When several masks have the same score, the lowest mask number wins.
    /// </remarks>
    private static bool[,] BuildMatrix(byte[] codewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var builder = new MicroQRMatrixBuilder(version);
        builder.PlaceFunctionPatternsAndData(codewords, ecLevel);

        var bestMask = 0;
        var bestScore = -1;

        for (var mask = 0; mask < 4; mask++)
        {
            builder.ApplyMask(mask);
            builder.PlaceFormatInfo(ecLevel, mask);
            var score = MicroQRMatrixBuilder.EvaluateMaskScore(builder.Modules, builder.Size);
            builder.ApplyMask(mask);

            if (score > bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        builder.ApplyMask(bestMask);
        builder.PlaceFormatInfo(ecLevel, bestMask);
        return builder.Modules;
    }
}
