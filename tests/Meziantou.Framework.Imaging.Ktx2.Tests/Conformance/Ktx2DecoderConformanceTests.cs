using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Decodes the KTX 2.0 files of <c>Files/</c>, written by KTX-Software, astcenc and basisu, and compares every subresource
/// with the SHA-256 of its reference image in <c>files.json</c>, which the reference tools decoded and cross-checked
/// (<c>tools/Meziantou.Framework.Imaging.CorpusGenerator/Ktx2Corpus.cs</c>, the <c>ktx2</c> generator). KTX 2.0 is a
/// random-access container reached through <see cref="ImageCollection"/>, like TIFF, so these files are not in the golden
/// corpus.
/// </summary>
public sealed class Ktx2DecoderConformanceTests
{
    private static readonly string FilesDirectory = Path.Combine(AppContext.BaseDirectory, "Files");

    private static JsonElement Manifest { get; } = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FilesDirectory, "files.json"))).RootElement.Clone();

    public static TheoryData<string> FileNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Manifest.GetProperty("files").EnumerateArray())
            {
                data.Add(file.GetProperty("file").GetString()!);
            }

            return data;
        }
    }

    private static JsonElement GetFile(string name)
    {
        foreach (var file in Manifest.GetProperty("files").EnumerateArray())
        {
            if (file.GetProperty("file").GetString() == name)
                return file;
        }

        throw new InvalidOperationException("The file is not listed: " + name);
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void EverySubresourceDecodesToItsReference(string name)
    {
        var file = GetFile(name);
        var data = File.ReadAllBytes(Path.Combine(FilesDirectory, name));
        Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageCollectionKind.TextureSurfaces, collection.Kind);
        foreach (var entry in file.GetProperty("entries").EnumerateArray())
        {
            var index = entry.GetProperty("index").GetInt32();
            var surface = Assert.IsType<TextureSurface>(collection[index].TextureSurface);
            Assert.Equal(entry.GetProperty("level").GetInt32(), surface.MipLevel);
            Assert.Equal(entry.GetProperty("layer").GetInt32(), surface.ArrayIndex);
            Assert.Equal(entry.GetProperty("face").GetInt32(), (int)(surface.Face ?? CubeMapFace.PositiveX));
            Assert.Equal(entry.GetProperty("slice").GetInt32(), surface.DepthSlice);
            Assert.Equal(new Size(entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32()), collection[index].Size);
            using var image = collection[index].Decode();
            Assert.Equal(image.PixelFormat, collection[index].PixelFormat);
            Assert.Equal(entry.GetProperty("sha256").GetString(), Hash(image, 0, entry.GetProperty("layout").GetString()!));
        }
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void ImageLoadDecodesTheFirstSubresourceOrTheAnimation(string name)
    {
        var file = GetFile(name);
        var data = File.ReadAllBytes(Path.Combine(FilesDirectory, name));
        using var image = Image.Load(data);
        var info = Image.Identify(data);
        Assert.Equal(image.Size, info.Size);
        Assert.Equal(image.PixelFormat, info.PixelFormat);
        var entries = file.GetProperty("entries").EnumerateArray().ToArray();
        if (image.IsAnimated)
        {
            // The layers of the base level are the frames
            Assert.HasCount(entries.Length, image.Frames);
            for (var i = 0; i < entries.Length; i++)
            {
                Assert.Equal(entries[i].GetProperty("sha256").GetString(), Hash(image, i, entries[i].GetProperty("layout").GetString()!));
            }

            return;
        }

        var first = entries.Single(entry => entry.GetProperty("index").GetInt32() == 0);
        Assert.Equal(first.GetProperty("sha256").GetString(), Hash(image, 0, first.GetProperty("layout").GetString()!));
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void DecodingReleasesEveryBuffer(string name)
    {
        var data = File.ReadAllBytes(Path.Combine(FilesDirectory, name));
        using var audit = new PoolAudit();
        using (Image.Load(data))
        {
        }

        using (var collection = ImageCollection.Load(data))
        {
            for (var i = 0; i < collection.Count; i++)
            {
                using var image = collection[i].Decode();
            }
        }

        audit.AssertClean(name);
    }

    [Theory]
    [InlineData("rgba32f-cube.ktx2")]
    [InlineData("rgba16f-array.ktx2")]
    [InlineData("bcn-bc6h-ufloat.ktx2")]
    [InlineData("astc-4x4-hdr.ktx2")]
    public void ATypedLoadToneMapsLikeCloneAs(string name)
    {
        // The peak of the tone mapping is the brightest sample of the image, whether the conversion runs at load or after it
        var data = File.ReadAllBytes(Path.Combine(FilesDirectory, name));
        var conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions(), OutOfRangeSamples = OutOfRangeSampleHandling.Clip };
        using var floats = Image.Load(data);
        using var expected = floats.CloneAs<Rgba64>(conversion);
        using var actual = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = conversion });
        var expectedPixels = new Rgba64[expected.Width * expected.Height];
        var actualPixels = new Rgba64[actual.Width * actual.Height];
        expected.Frames[0].CopyPixelDataTo(expectedPixels);
        actual.Frames[0].CopyPixelDataTo(actualPixels);
        Assert.Equal(expectedPixels, actualPixels);
    }

    private static string Hash(Image image, int frame, string layout)
    {
        // The reference layouts: RGBA of 8 or 16 bits, of halves or of floats, rows from the top
        using var converted = layout switch
        {
            "rgba8" => (Image)image.CloneAs<Rgba32>(),
            "rgba16le" => image.CloneAs<Rgba64>(),
            "rgbaf16le" => image.CloneAs<RgbaHalf>(),
            "rgbaf32le" => image.CloneAs<RgbaFloat>(),
            _ => throw new InvalidOperationException("Unknown layout " + layout),
        };
        var bytes = new byte[converted.Width * converted.Height * PixelFormats.GetBytesPerPixel(converted.PixelFormat)];
        converted.Frames[frame].CopyPixelBytesTo(bytes);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
