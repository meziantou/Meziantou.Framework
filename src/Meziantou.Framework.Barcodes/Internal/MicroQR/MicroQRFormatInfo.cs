using System.Numerics;

namespace Meziantou.Framework.Barcodes.Internal.MicroQR;

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

    /// <summary>
    /// Finds the format information of a symbol of <paramref name="version"/> closest to the bits read from it. BCH(15,5)
    /// corrects up to 3 bit errors.
    /// </summary>
    public static bool TryDecode(int bits, int version, out ErrorCorrectionLevel ecLevel, out int maskPattern)
    {
        ecLevel = default;
        maskPattern = -1;
        var bestDistance = int.MaxValue;
        foreach (var level in (ReadOnlySpan<ErrorCorrectionLevel>)[ErrorCorrectionLevel.L, ErrorCorrectionLevel.M, ErrorCorrectionLevel.Q])
        {
            if (!MicroQRVersion.IsECLevelSupported(version, level) || MicroQRVersion.ResolveECLevel(version, level) != level)
                continue;

            var symbolNumber = MicroQRVersion.GetSymbolNumber(version, level);
            for (var mask = 0; mask < 4; mask++)
            {
                var distance = BitOperations.PopCount((uint)(GetFormatInfo(symbolNumber, mask) ^ bits));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    ecLevel = level;
                    maskPattern = mask;
                }
            }
        }

        return bestDistance <= 3;
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
