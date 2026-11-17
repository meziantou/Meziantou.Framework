using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Barcodes;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Benchmarks;

[MemoryDiagnoser]
public class RenderingBenchmarks
{
    private readonly QRCode _qrCode = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.M);
    private readonly Barcode _barcode = Barcode.CreateCode128("Meziantou.Framework.Barcodes");
    private readonly Barcode2D _dataMatrix = Barcode2D.CreateDataMatrix("Meziantou.Framework.Barcodes");

    [Params(1, 4, 16)]
    public int Scale { get; set; }

    private QRCodeRenderOptions QRCodeOptions => new() { ModuleSize = 10 * Scale };

    private BarcodeRenderOptions BarcodeOptions => new() { ModuleWidth = 2 * Scale, ModuleHeight = 80 * Scale };

    private Barcode2DRenderOptions Barcode2DOptions => new() { ModuleSize = 4 * Scale };

    [Benchmark]
    public Scene QRCode_CreateScene() => _qrCode.CreateScene(QRCodeOptions);

    [Benchmark]
    public string QRCode_ToSvg() => _qrCode.ToSvg(QRCodeOptions);

    [Benchmark]
    public void QRCode_ToPng()
    {
        using var image = _qrCode.ToImage(QRCodeOptions);
        image.Save(Stream.Null, new PngEncoder());
    }

    [Benchmark]
    public Scene Barcode_CreateScene() => _barcode.CreateScene(BarcodeOptions);

    [Benchmark]
    public string Barcode_ToSvg() => _barcode.ToSvg(BarcodeOptions);

    [Benchmark]
    public void Barcode_ToPng()
    {
        using var image = _barcode.ToImage(BarcodeOptions);
        image.Save(Stream.Null, new PngEncoder());
    }

    [Benchmark]
    public string Barcode2D_ToSvg() => _dataMatrix.ToSvg(Barcode2DOptions);

    [Benchmark]
    public void Barcode2D_ToPng()
    {
        using var image = _dataMatrix.ToImage(Barcode2DOptions);
        image.Save(Stream.Null, new PngEncoder());
    }
}
