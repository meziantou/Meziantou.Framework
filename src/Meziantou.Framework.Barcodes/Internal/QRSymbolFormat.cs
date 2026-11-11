using Meziantou.Framework.Barcodes.Internal.MicroQR;
using Meziantou.Framework.Barcodes.Internal.RMQR;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// The sizes of the mode and character count indicators of the data segments, in standard QR, Micro QR and rMQR codes.
/// </summary>
internal static class QRSymbolFormat
{
    public static int GetModeIndicatorBits(QRCodeType type, int version)
    {
        return type switch
        {
            QRCodeType.Standard => 4,
            QRCodeType.MicroQR => MicroQRVersion.GetModeIndicatorBits(version),
            _ => RMQRVersion.GetModeIndicatorBits(),
        };
    }

    public static int GetModeIndicatorValue(QRCodeType type, int version, EncodingMode mode)
    {
        return type switch
        {
            QRCodeType.Standard => (int)mode,
            QRCodeType.MicroQR => MicroQRVersion.GetModeIndicatorValue(version, mode),
            _ => RMQRVersion.GetModeIndicatorValue(mode),
        };
    }

    /// <summary>Gets the number of bits of the character count indicator, or -1 when the symbol does not support the mode.</summary>
    public static int GetCharacterCountBits(QRCodeType type, int version, EncodingMode mode)
    {
        if (mode is not (EncodingMode.Numeric or EncodingMode.Alphanumeric or EncodingMode.Byte or EncodingMode.Kanji))
            return -1;

        return type switch
        {
            QRCodeType.Standard => QRCodeVersion.GetCharacterCountBits(version, mode),
            QRCodeType.MicroQR => MicroQRVersion.IsModeSupported(version, mode) ? MicroQRVersion.GetCharacterCountBits(version, mode) : -1,
            _ => RMQRVersion.GetCharacterCountBits(version, mode),
        };
    }

    /// <summary>
    /// Gets the number of bits of segments with their indicators, or -1 when the symbol does not support one of their
    /// modes, a count does not fit in its indicator, or the symbol cannot hold that many segments.
    /// </summary>
    public static int GetBitCount(IReadOnlyList<QRCodeSegment> segments, QRCodeType type, int version)
    {
        // M1 has no mode indicator: its data is a single numeric segment
        if (type is QRCodeType.MicroQR && version is 1 && segments.Count is not 1)
            return -1;

        var bitCount = 0L;
        foreach (var segment in segments)
        {
            var segmentBitCount = segment.GetBitCount(type, version);
            if (segmentBitCount < 0)
                return -1;

            bitCount += segmentBitCount;
            if (bitCount > int.MaxValue)
                return -1;
        }

        return (int)bitCount;
    }

    public static void Write(BitBuffer buffer, IReadOnlyList<QRCodeSegment> segments, QRCodeType type, int version)
    {
        foreach (var segment in segments)
        {
            segment.Write(buffer, type, version);
        }
    }
}
