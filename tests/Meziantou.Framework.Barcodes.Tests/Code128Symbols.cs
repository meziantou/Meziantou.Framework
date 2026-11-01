using System.Numerics;

namespace Meziantou.Framework.Barcodes.Tests;

/// <summary>Reads the codewords of a Code 128 symbol back from its modules, with the bar and space widths of ISO/IEC 15417 Table 1.</summary>
internal static class Code128Symbols
{
    private static readonly string[] Widths =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    /// <summary>Returns the codewords from the start character to the stop character included.</summary>
    public static int[] Decode(Barcode barcode)
    {
        var runs = new List<int>();
        for (var column = 0; column < barcode.Width; column++)
        {
            if (column > 0 && barcode[0, column] == barcode[0, column - 1])
            {
                runs[^1]++;
            }
            else
            {
                runs.Add(1);
            }
        }

        var codewords = new List<int>();
        var index = 0;
        while (index < runs.Count)
        {
            var length = runs.Count - index == 7 ? 7 : 6;
            var widths = string.Concat(runs.Skip(index).Take(length));
            var codeword = Array.IndexOf(Widths, widths);
            Assert.True(codeword >= 0, $"Unknown symbol character {widths} at bar {index}");
            codewords.Add(codeword);
            index += length;
        }

        return [.. codewords];
    }

    /// <summary>Checks the symbol check character (the codeword before the stop character) with arbitrary-precision arithmetic.</summary>
    public static void AssertValidChecksum(int[] codewords)
    {
        var sum = new BigInteger(codewords[0]);
        for (var i = 1; i < codewords.Length - 2; i++)
        {
            sum += new BigInteger(codewords[i]) * i;
        }

        Assert.Equal((int)(sum % 103), codewords[^2]);
        Assert.Equal(106, codewords[^1]);
    }
}
