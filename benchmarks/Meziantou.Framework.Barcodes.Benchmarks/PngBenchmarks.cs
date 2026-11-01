using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Barcodes.Benchmarks;

[MemoryDiagnoser]
public class PngBenchmarks
{
    private readonly QRCode _qrCode = QRCode.Create("https://www.meziantou.net/", ErrorCorrectionLevel.M);
    private readonly Barcode _barcode = Barcode.CreateCode128("Meziantou.Framework.Barcodes");

    [Params(1, 4, 16)]
    public int Scale { get; set; }

    [Benchmark]
    public void QRCode_WriteToPng()
    {
        _qrCode.WriteToPng(Stream.Null, new QRCodePngOptions { ModuleSize = 10 * Scale });
    }

    [Benchmark]
    public void Barcode_WriteToPng()
    {
        _barcode.WriteToPng(Stream.Null, new BarcodePngOptions { ModuleWidth = 2 * Scale, ModuleHeight = 80 * Scale });
    }
}
