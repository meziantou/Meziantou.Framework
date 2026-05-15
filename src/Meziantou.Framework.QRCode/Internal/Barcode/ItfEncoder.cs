namespace Meziantou.Framework.Internal.Barcodes;

internal static class ItfEncoder
{
    private const int NarrowWidth = 1;
    private const int WideWidth = 3;

    private static readonly int[] StartPattern = [NarrowWidth, NarrowWidth, NarrowWidth, NarrowWidth];
    private static readonly int[] EndPattern = [WideWidth, NarrowWidth, NarrowWidth];
    private static readonly int[][] Patterns =
    [
        [NarrowWidth, NarrowWidth, WideWidth, WideWidth, NarrowWidth], // 0
        [WideWidth, NarrowWidth, NarrowWidth, NarrowWidth, WideWidth], // 1
        [NarrowWidth, WideWidth, NarrowWidth, NarrowWidth, WideWidth], // 2
        [WideWidth, WideWidth, NarrowWidth, NarrowWidth, NarrowWidth], // 3
        [NarrowWidth, NarrowWidth, WideWidth, NarrowWidth, WideWidth], // 4
        [WideWidth, NarrowWidth, WideWidth, NarrowWidth, NarrowWidth], // 5
        [NarrowWidth, WideWidth, WideWidth, NarrowWidth, NarrowWidth], // 6
        [NarrowWidth, NarrowWidth, NarrowWidth, WideWidth, WideWidth], // 7
        [WideWidth, NarrowWidth, NarrowWidth, WideWidth, NarrowWidth], // 8
        [NarrowWidth, WideWidth, NarrowWidth, WideWidth, NarrowWidth], // 9
    ];

    public static Barcode Encode(string data)
    {
        ValidateData(data);

        var modules = new List<bool>(9 + (data.Length * 9));
        AppendPattern(modules, StartPattern, startDark: true);

        for (var i = 0; i < data.Length; i += 2)
        {
            var firstDigit = data[i] - '0';
            var secondDigit = data[i + 1] - '0';

            for (var j = 0; j < 5; j++)
            {
                BarcodeEncoderHelper.AppendModules(modules, isDark: true, Patterns[firstDigit][j]);
                BarcodeEncoderHelper.AppendModules(modules, isDark: false, Patterns[secondDigit][j]);
            }
        }

        AppendPattern(modules, EndPattern, startDark: true);
        return BarcodeEncoderHelper.CreateBarcode(BarcodeType.Itf, modules);
    }

    private static void ValidateData(string data)
    {
        if ((data.Length & 1) == 1)
        {
            throw new ArgumentException("The data length must be even for ITF.", nameof(data));
        }

        foreach (var character in data)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new ArgumentException($"The character '{character}' is not supported by ITF.", nameof(data));
            }
        }
    }

    private static void AppendPattern(List<bool> modules, int[] pattern, bool startDark)
    {
        var isDark = startDark;
        foreach (var width in pattern)
        {
            BarcodeEncoderHelper.AppendModules(modules, isDark, width);
            isDark = !isDark;
        }
    }
}
