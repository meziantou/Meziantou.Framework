namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>
/// Reads EAN-13, UPC-A, EAN-8 and UPC-E symbols, and their 2- and 5-digit add-ons (GS1 General Specifications, 5.2).
/// An EAN-13 symbol whose first digit is 0 is a UPC-A symbol: the two are the same bars.
/// </summary>
internal static class EanUpcDecoder
{
    // The digits of the left half start with a light element (sets L and G), the ones of the right half with a dark one (set R)
    private static readonly int[][] LeftPatterns = [.. EanUpcEncoder.LPatterns.Select(RunPatterns.FromModules), .. EanUpcEncoder.GPatterns.Select(RunPatterns.FromModules)];
    private static readonly int[][] RightPatterns = [.. EanUpcEncoder.RPatterns.Select(RunPatterns.FromModules)];
    private static readonly int[] Guard = [1, 1, 1];
    private static readonly int[] MiddleGuard = [1, 1, 1, 1, 1];
    private static readonly int[] UpcEEndGuard = [1, 1, 1, 1, 1, 1];
    private static readonly int[] AddOnStart = [1, 1, 2];
    private static readonly int[] AddOnSeparator = [1, 1];

    public static LinearMatch? TryDecode(ScanLine line, int start)
    {
        if (!RunPatterns.Matches(line, start, Guard))
            return null;

        return TryDecodeEan13(line, start) ?? TryDecodeEan8(line, start) ?? TryDecodeUpcE(line, start);
    }

    private static LinearMatch? TryDecodeEan13(ScanLine line, int start)
    {
        // Start guard, 6 digits of 4 elements, middle guard, 6 digits, end guard: 59 elements, 95 modules
        if (start + 59 > line.Count || !RunPatterns.Matches(line, start + 27, MiddleGuard) || !RunPatterns.Matches(line, start + 56, Guard) || !HasRegularDigits(line, start, 59, 95, 6, 32, 6))
            return null;

        Span<char> digits = stackalloc char[13];
        var parity = 0;
        for (var i = 0; i < 6; i++)
        {
            var value = RunPatterns.FindBest(line, start + 3 + (i * 4), LeftPatterns, 7);
            if (value < 0)
                return null;

            digits[i + 1] = (char)('0' + (value % 10));
            parity = (parity << 1) | (value >= 10 ? 1 : 0);
        }

        if (!ReadRightDigits(line, start + 32, digits[7..]))
            return null;

        // The parities of the left digits give the first digit
        var firstDigit = Array.IndexOf(EanUpcEncoder.Ean13FirstDigitEncodings, parity);
        if (firstDigit < 0)
            return null;

        digits[0] = (char)('0' + firstDigit);
        if (EanUpcEncoder.ComputeChecksumDigit(digits[..12]) != digits[12] - '0')
            return null;

        var moduleWidth = line.GetWidth(start, 59) / 95;
        var text = firstDigit is 0 ? new string(digits[1..]) : new string(digits);
        return AppendAddOn(line, firstDigit is 0 ? BarcodeType.UpcA : BarcodeType.Ean13, text, start, start + 59, moduleWidth);
    }

    private static LinearMatch? TryDecodeEan8(ScanLine line, int start)
    {
        // Start guard, 4 digits, middle guard, 4 digits, end guard: 43 elements, 67 modules
        if (start + 43 > line.Count || !RunPatterns.Matches(line, start + 19, MiddleGuard) || !RunPatterns.Matches(line, start + 40, Guard) || !HasRegularDigits(line, start, 43, 67, 4, 24, 4))
            return null;

        Span<char> digits = stackalloc char[8];
        for (var i = 0; i < 4; i++)
        {
            var value = RunPatterns.FindBest(line, start + 3 + (i * 4), LeftPatterns, 7);
            if (value is < 0 or >= 10)
                return null;

            digits[i] = (char)('0' + value);
        }

        if (!ReadRightDigits(line, start + 24, digits[4..]))
            return null;

        if (EanUpcEncoder.ComputeChecksumDigit(digits[..7]) != digits[7] - '0')
            return null;

        return AppendAddOn(line, BarcodeType.Ean8, new string(digits), start, start + 43, line.GetWidth(start, 43) / 67);
    }

    private static LinearMatch? TryDecodeUpcE(ScanLine line, int start)
    {
        // Start guard, 6 digits, end guard of 6 elements: 33 elements, 51 modules
        if (start + 33 > line.Count || !RunPatterns.Matches(line, start + 27, UpcEEndGuard) || !HasRegularDigits(line, start, 33, 51, 6, 0, 0))
            return null;

        Span<char> digits = stackalloc char[6];
        var parity = 0;
        for (var i = 0; i < 6; i++)
        {
            var value = RunPatterns.FindBest(line, start + 3 + (i * 4), LeftPatterns, 7);
            if (value < 0)
                return null;

            digits[i] = (char)('0' + (value % 10));
            parity = (parity << 1) | (value >= 10 ? 1 : 0);
        }

        // The parities give the number system (0: the encodings of the table, 1: their complements) and the check digit
        for (var numberSystem = 0; numberSystem < 2; numberSystem++)
        {
            var checkDigit = Array.IndexOf(EanUpcEncoder.UpcENumberSystem0Encodings, numberSystem is 0 ? parity : parity ^ 0x3F);
            if (checkDigit < 0)
                continue;

            var upcA = EanUpcEncoder.ExpandUpcE((char)('0' + numberSystem), new string(digits));
            if (EanUpcEncoder.ComputeChecksumDigit(upcA) != checkDigit)
                continue;

            var text = string.Concat(((char)('0' + numberSystem)).ToString(), new string(digits), ((char)('0' + checkDigit)).ToString());
            return AppendAddOn(line, BarcodeType.UpcE, text, start, start + 33, line.GetWidth(start, 33) / 51);
        }

        return null;
    }

    // Each digit is 7 modules wide: the digits of the symbol have the width of 7 of its modules, within a module
    private static bool HasRegularDigits(ScanLine line, int start, int runCount, int modules, int leftDigits, int rightStart, int rightDigits)
    {
        var moduleWidth = line.GetWidth(start, runCount) / modules;
        for (var i = 0; i < leftDigits + rightDigits; i++)
        {
            var position = i < leftDigits ? start + 3 + (i * 4) : start + rightStart + ((i - leftDigits) * 4);
            if (Math.Abs((line.GetWidth(position, 4) / moduleWidth) - 7) > 1)
                return false;
        }

        return true;
    }

    private static bool ReadRightDigits(ScanLine line, int start, Span<char> digits)
    {
        for (var i = 0; i < digits.Length; i++)
        {
            var value = RunPatterns.FindBest(line, start + (i * 4), RightPatterns, 7);
            if (value < 0)
                return false;

            digits[i] = (char)('0' + value);
        }

        return true;
    }

    // The add-on follows the symbol after a gap of 7 to 12 modules: a start pattern 1011, then the digits separated by 01
    private static LinearMatch AppendAddOn(ScanLine line, BarcodeType type, string text, int start, int end, double moduleWidth)
    {
        var match = new LinearMatch(type, text, start, end, moduleWidth);
        if (end + 1 >= line.Count)
            return match;

        var gap = line.Runs[end] / moduleWidth;
        if (gap is < 5 or > 15)
            return match;

        foreach (var length in (ReadOnlySpan<int>)[5, 2])
        {
            var addOn = TryReadAddOn(line, end + 1, length, moduleWidth);
            if (addOn is not null)
                return match with { Text = text + " " + addOn, End = end + 1 + 3 + (length * 4) + ((length - 1) * 2) };
        }

        return match;
    }

    private static string? TryReadAddOn(ScanLine line, int start, int length, double moduleWidth)
    {
        var runs = 3 + (length * 4) + ((length - 1) * 2);
        if (start + runs > line.Count || !RunPatterns.Matches(line, start, AddOnStart))
            return null;

        // The add-on is followed by the quiet zone
        if (start + runs < line.Count && line.Runs[start + runs] < moduleWidth * 3)
            return null;

        Span<char> digits = stackalloc char[length];
        var parity = 0;
        var position = start + 3;
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                if (!RunPatterns.Matches(line, position, AddOnSeparator))
                    return null;

                position += 2;
            }

            var value = RunPatterns.FindBest(line, position, LeftPatterns, 7);
            if (value < 0)
                return null;

            digits[i] = (char)('0' + (value % 10));
            parity = (parity << 1) | (value >= 10 ? 1 : 0);
            position += 4;
        }

        var addOn = new string(digits);
        var expectedParity = length is 2
            ? int.Parse(addOn, NumberStyles.None, CultureInfo.InvariantCulture) % 4
            : EanUpcEncoder.Extension5CheckDigitEncodings[EanUpcEncoder.ComputeFiveDigitExtensionChecksum(addOn)];
        return parity == expectedParity ? addOn : null;
    }
}
