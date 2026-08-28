namespace Meziantou.Framework.Tests;

internal static class BarcodeModulePattern
{
    /// <summary>
    /// Renders a barcode as one character per module, <c>1</c> for a bar and <c>0</c> for a space.
    /// </summary>
    public static string Render(Barcode barcode)
    {
        var sb = new StringBuilder(barcode.Width);
        for (var column = 0; column < barcode.Width; column++)
        {
            sb.Append(barcode[0, column] ? '1' : '0');
        }

        return sb.ToString();
    }
}
