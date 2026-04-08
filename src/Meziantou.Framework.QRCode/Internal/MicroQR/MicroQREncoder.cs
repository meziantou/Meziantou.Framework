using System.Text;

namespace Meziantou.Framework.Internal.MicroQR;

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

        return new QRCode(builder.Modules, version, QRCodeType.MicroQR);
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
        var totalDataBits = MicroQRVersion.GetDataCodewords(version, ecLevel) * 8;
        var terminatorMaxBits = MicroQRVersion.GetTerminatorBits(version);
        DataEncoderHelper.AddTerminatorAndPadding(buffer, totalDataBits, terminatorMaxBits);

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

    private static int FindBestMask(byte[] codewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var bestMask = 0;
        var bestScore = -1;

        for (var mask = 0; mask < 4; mask++)
        {
            var builder = new MicroQRMatrixBuilder(version);
            builder.Build(codewords, ecLevel, mask);
            var score = MicroQRMatrixBuilder.EvaluateMaskPenalty(builder.Modules, builder.Size);

            if (score > bestScore)
            {
                bestScore = score;
                bestMask = mask;
            }
        }

        return bestMask;
    }
}
