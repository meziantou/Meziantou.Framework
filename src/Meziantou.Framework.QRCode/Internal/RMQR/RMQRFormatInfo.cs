namespace Meziantou.Framework.Internal.RMQR;

internal static class RMQRFormatInfo
{
    // BCH(18,6) for rMQR format information.
    // 6 data bits: 1 bit EC type (0=M, 1=H) | 5 bits version number (0-31)
    // Generator polynomial: x^12 + x^10 + x^8 + x^5 + x^4 + x^2 + 1 = 0x1577
    // Mask: 0x20A7B (applied via XOR after BCH encoding)
    private const int GeneratorPolynomial = 0x1577;
    private const int FormatMask = 0x20A7B;

    // Encode 18-bit format information from EC level and version.
    public static int GetFormatInfo(ErrorCorrectionLevel ecLevel, int version)
    {
        var ecBit = ecLevel switch
        {
            ErrorCorrectionLevel.M => 0,
            ErrorCorrectionLevel.H => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(ecLevel)),
        };

        var dataBits = (ecBit << 5) | (version & 0x1F);
        var encoded = ComputeBCH18_6(dataBits);
        return encoded ^ FormatMask;
    }

    private static int ComputeBCH18_6(int data)
    {
        // Place 6 data bits at positions 17..12
        var bits = data << 12;
        var remainder = bits;

        // Polynomial long division (6 data bits -> 12 check bits)
        for (var i = 5; i >= 0; i--)
        {
            if ((remainder & (1 << (i + 12))) != 0)
            {
                remainder ^= GeneratorPolynomial << i;
            }
        }

        return bits | remainder;
    }
}
