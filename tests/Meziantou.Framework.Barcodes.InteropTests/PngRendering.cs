using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.VectorGraphics;

namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>Renders the symbols as the PNG files the external readers take.</summary>
internal static class PngRendering
{
    public static byte[] ToPngBytes(this QRCode qrCode, QRCodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        using var image = qrCode.ToImage(options, imageOptions);
        return Encode(image);
    }

    public static byte[] ToPngBytes(this Barcode barcode, BarcodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        using var image = barcode.ToImage(options, imageOptions);
        return Encode(image);
    }

    public static byte[] ToPngBytes(this Barcode2D barcode, Barcode2DRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        using var image = barcode.ToImage(options, imageOptions);
        return Encode(image);
    }

    public static byte[] Encode(Image<Rgba32> image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
