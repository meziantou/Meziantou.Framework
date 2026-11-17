using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Barcodes.Tests;

/// <summary>Renders the scenes of the symbols as PNG files, as the image renderer draws them.</summary>
internal static class PngRendering
{
    public static byte[] ToPng(this QRCode qrCode, QRCodeRenderOptions? options = null)
    {
        using var image = qrCode.ToImage(options);
        return Encode(image);
    }

    public static byte[] ToPng(this Barcode barcode, BarcodeRenderOptions? options = null)
    {
        using var image = barcode.ToImage(options);
        return Encode(image);
    }

    public static byte[] ToPng(this Barcode2D barcode, Barcode2DRenderOptions? options = null)
    {
        using var image = barcode.ToImage(options);
        return Encode(image);
    }

    public static byte[] Encode(Imaging.Image image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
