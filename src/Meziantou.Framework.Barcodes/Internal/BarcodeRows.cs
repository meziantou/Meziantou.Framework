namespace Meziantou.Framework.Barcodes.Internal;

internal static class BarcodeRows
{
    // The number of rows, starting at `row`, that have the same modules
    public static int CountIdenticalRows(Barcode barcode, int row)
    {
        var count = 1;
        while (row + count < barcode.Height && AreIdentical(barcode, row, row + count))
        {
            count++;
        }

        return count;
    }

    private static bool AreIdentical(Barcode barcode, int row1, int row2)
    {
        for (var column = 0; column < barcode.Width; column++)
        {
            if (barcode[row1, column] != barcode[row2, column])
                return false;
        }

        return true;
    }
}
