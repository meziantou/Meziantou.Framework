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
        var dataCWCount = RMQRVersion.GetDataCodewords(version, ecLevel);
        var ecCWCount = RMQRVersion.GetECCodewords(version, ecLevel);
        var totalCWCount = RMQRVersion.GetTotalCodewords(version);

        // rMQR uses a single RS block
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
}
