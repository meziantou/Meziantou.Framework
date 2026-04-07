namespace Meziantou.Framework.Internal.MicroQR;

internal static class MicroQRFormatInfo
{
    // BCH(15,5) generator polynomial: x^10 + x^8 + x^5 + x^4 + x^2 + x + 1 = 0x537
    private const int GeneratorPolynomial = 0x537;

    // Final XOR mask for Micro QR format information
    private const int FormatMask = 0x4445;

    // Encode 15-bit format information from symbol number (3 bits) and mask pattern (2 bits).
    // The 5 data bits are: symbol_number[2:0] << 2 | mask_pattern[1:0]
    public static int GetFormatInfo(int symbolNumber, int maskPattern)
    {
        var dataBits = (symbolNumber << 2) | maskPattern;
        var encoded = ComputeBCH15_5(dataBits);
        return encoded ^ FormatMask;
    }

    private static int ComputeBCH15_5(int data)
    {
        // Place 5 data bits at positions 14..10
        var bits = data << 10;
        var remainder = bits;

        // Polynomial long division
        for (var i = 4; i >= 0; i--)
        {
            if ((remainder & (1 << (i + 10))) != 0)
            {
                remainder ^= GeneratorPolynomial << i;
            }
        }

        return bits | remainder;
    }
}
