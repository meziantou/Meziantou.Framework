namespace Meziantou.Framework.Barcodes.Internal.Linear;

internal static class BarcodeEncoderHelper
{
    public static Barcode CreateBarcode(BarcodeType type, List<bool> modules, string text)
    {
        var matrix = new bool[1, modules.Count];
        for (var col = 0; col < modules.Count; col++)
        {
            matrix[0, col] = modules[col];
        }

        return new Barcode(matrix, type, text);
    }

    public static void AppendModules(List<bool> modules, bool isDark, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        for (var i = 0; i < width; i++)
        {
            modules.Add(isDark);
        }
    }
}
