namespace Meziantou.Framework.Barcodes.Internal.Linear;

// Shared parts of the GS1 DataBar symbologies (ISO/IEC 24724:2011)
internal static class DataBarHelper
{
    // ISO/IEC 24724:2011, Annex B: the widths of the elements of a character of the "n, k" family whose value is
    // `value`. The elements are `n` modules wide in total, each element is at most `maxWidth` modules wide, and at least
    // one element is 1 module wide unless `noNarrow` is set.
    public static void GetWidths(Span<int> widths, int value, int modules, int maxWidth, bool noNarrow)
    {
        var elements = widths.Length;
        var narrowMask = 0;
        int bar;
        for (bar = 0; bar < elements - 1; bar++)
        {
            int elementWidth;
            int subValue;
            for (elementWidth = 1, narrowMask |= 1 << bar; ; elementWidth++, narrowMask &= ~(1 << bar))
            {
                // All the combinations
                subValue = Combinations(modules - elementWidth - 1, elements - bar - 2);

                // Less the combinations with no single-module element
                if (!noNarrow && narrowMask == 0 && modules - elementWidth - (elements - bar - 1) >= elements - bar - 1)
                {
                    subValue -= Combinations(modules - elementWidth - (elements - bar), elements - bar - 2);
                }

                // Less the combinations with elements wider than maxWidth
                if (elements - bar - 1 > 1)
                {
                    var lessValue = 0;
                    for (var widestElement = modules - elementWidth - (elements - bar - 2); widestElement > maxWidth; widestElement--)
                    {
                        lessValue += Combinations(modules - elementWidth - widestElement - 1, elements - bar - 3);
                    }

                    subValue -= lessValue * (elements - 1 - bar);
                }
                else if (modules - elementWidth > maxWidth)
                {
                    subValue--;
                }

                value -= subValue;
                if (value < 0)
                    break;
            }

            value += subValue;
            modules -= elementWidth;
            widths[bar] = elementWidth;
        }

        widths[bar] = modules;
    }

    // The weight of the element `index` in the checksum of a DataBar symbol: 3^index modulo `modulus`
    public static int GetChecksumWeight(int index, int modulus)
    {
        var weight = 1;
        for (var i = 0; i < index; i++)
        {
            weight = weight * 3 % modulus;
        }

        return weight;
    }

    // Validates a GTIN of 13 digits (without check digit) or 14 digits (with check digit) and returns its 14 digits
    public static string NormalizeGtin(string data, string symbology)
    {
        foreach (var character in data)
        {
            if (!char.IsAsciiDigit(character))
                throw new ArgumentException($"The value contains invalid character '{character}' for {symbology}.", nameof(data));
        }

        if (data.Length == 13)
            return string.Concat(data, ((char)('0' + ComputeGtinCheckDigit(data.AsSpan()))).ToString());

        if (data.Length == 14)
        {
            if (ComputeGtinCheckDigit(data.AsSpan(0, 13)) != data[13] - '0')
                throw new ArgumentException($"The data is not a valid {symbology} value because the check digit is invalid.", nameof(data));

            return data;
        }

        throw new ArgumentException($"The data should be a GTIN of 13 digits (without check digit) or 14 digits (with check digit) for {symbology}.", nameof(data));
    }

    // The GS1 mod 10 check digit (GS1 General Specifications, section 7.9.1)
    public static int ComputeGtinCheckDigit(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var useWeight3 = true;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            sum += (digits[i] - '0') * (useWeight3 ? 3 : 1);
            useWeight3 = !useWeight3;
        }

        return (10 - (sum % 10)) % 10;
    }

    // Appends the modules of elements of alternating colors, the first one dark or light
    public static void AppendElements(List<bool> modules, ReadOnlySpan<int> widths, bool firstIsDark)
    {
        var isDark = firstIsDark;
        foreach (var width in widths)
        {
            BarcodeEncoderHelper.AppendModules(modules, isDark, width);
            isDark = !isDark;
        }
    }

    // Creates a stacked barcode: each row is repeated as many times as it is modules high
    public static Barcode CreateStackedBarcode(BarcodeType type, IReadOnlyList<(bool[] Modules, int Height)> rows, string text)
    {
        var width = rows[0].Modules.Length;
        var height = 0;
        foreach (var row in rows)
        {
            height += row.Height;
        }

        var matrix = new bool[height, width];
        var y = 0;
        foreach (var (modules, rowHeight) in rows)
        {
            for (var i = 0; i < rowHeight; i++, y++)
            {
                for (var x = 0; x < modules.Length; x++)
                {
                    matrix[y, x] = modules[x];
                }
            }
        }

        return new Barcode(matrix, type, text, isStacked: true);
    }

    private static int Combinations(int n, int r)
    {
        int minDenominator, maxDenominator;
        if (n - r > r)
        {
            minDenominator = r;
            maxDenominator = n - r;
        }
        else
        {
            minDenominator = n - r;
            maxDenominator = r;
        }

        long value = 1;
        var j = 1;
        for (var i = n; i > maxDenominator; i--)
        {
            value *= i;
            if (j <= minDenominator)
            {
                value /= j;
                j++;
            }
        }

        for (; j <= minDenominator; j++)
        {
            value /= j;
        }

        return (int)value;
    }
}
