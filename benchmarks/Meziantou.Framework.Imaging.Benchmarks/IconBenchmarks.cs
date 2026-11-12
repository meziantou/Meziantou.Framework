using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Windows icons with the usual representations (16x16, 32x32 and 48x48 as 32-bit bitmaps, 256x256 as PNG), made from the
/// center of the 1024x768 RGBA source with transparency: writing them from images of these sizes, the whole favicon
/// pipeline (crop, four resizes, save), and reading back the representation a 24x24 display picks.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class IconBenchmarks : IDisposable
{
    private static readonly int[] Sides = [16, 32, 48, 256];

    private readonly MemoryStream _output = new();
    private Image<Rgba32> _source = null!;
    private ImageCollection _representations = null!;
    private byte[] _icon = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);
        _representations = CreateRepresentations(_source);
        _representations.Save(_output, new IcoEncoder());
        _icon = _output.ToArray();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source?.Dispose();
        _representations?.Dispose();
    }

    [Benchmark]
    [Workload("ICO of 4 representations (16, 32, 48 as bitmaps, 256 as PNG) -> 32x32 Rgba32 for a 24x24 display")]
    public int DecodeIco()
    {
        using var icon = ImageCollection.Load(_icon);
        using var image = icon.SelectBySize(new Size(24, 24)).Decode();
        return image.Width;
    }

    [Benchmark]
    [Workload("4 Rgba32 representations (16, 32, 48, 256) -> ICO", ReturnsOutputBytes = true)]
    public long EncodeIco()
    {
        _output.SetLength(0);
        _representations.Save(_output, new IcoEncoder());
        return _output.Length;
    }

    [Benchmark]
    [Workload("1024x768 Rgba32 -> center 768x768 -> 4 resizes (Catmull-Rom) -> ICO", ReturnsOutputBytes = true)]
    public long CreateFavicon()
    {
        using var representations = CreateRepresentations(_source);
        _output.SetLength(0);
        representations.Save(_output, new IcoEncoder());
        return _output.Length;
    }

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ImageCollection CreateRepresentations(Image<Rgba32> source)
    {
        var representations = ImageCollection.Create(ImageCollectionKind.Representations);
        try
        {
            using var square = source.Clone();
            square.Crop(new Rectangle((source.Width - source.Height) / 2, 0, source.Height, source.Height));
            foreach (var side in Sides)
            {
                using var sized = square.Clone();
                sized.Resize(new ResizeOptions(side, side) { Mode = ResizeMode.Stretch });
                representations.Add(sized);
            }

            return representations;
        }
        catch
        {
            representations.Dispose();
            throw;
        }
    }
}
