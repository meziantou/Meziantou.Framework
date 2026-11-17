using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The DDS fixtures of the golden corpus cover what the decoder reads: the generic suites compare the image of every valid
/// fixture (the top level of the first face or element) with its reference, bit for bit (<c>EagerLoadConformanceTests</c>,
/// <c>SequentialReadConformanceTests</c>), and these tests compare every surface of the textures of several surfaces, read
/// through <see cref="ImageCollection"/>, check that every format and structure has fixtures, and that the files the
/// decoder rejects are rejected for the reason their fixture declares.
/// </summary>
public sealed class DdsDecoderConformanceTests
{
    public static TheoryData<string> SurfaceIds => [.. GoldenCorpus.Default.GetIds(format: "dds", kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.DdsSurfaces is not null)];

    public static TheoryData<string> ValidIds => [.. GoldenCorpus.Default.GetIds(format: "dds", kind: FixtureKinds.Valid)];

    [Theory]
    [InlineData("dds.format=bc1")]
    [InlineData("dds.format=bc2")]
    [InlineData("dds.format=bc3")]
    [InlineData("dds.format=bc4")]
    [InlineData("dds.format=bc4s")]
    [InlineData("dds.format=bc5")]
    [InlineData("dds.format=bc5s")]
    [InlineData("dds.format=bc6h")]
    [InlineData("dds.format=bc6hs")]
    [InlineData("dds.format=bc7")]
    [InlineData("dds.format=r8g8b8a8-unorm")]
    [InlineData("dds.format=r8g8b8a8-unorm-srgb")]
    [InlineData("dds.format=b8g8r8a8-unorm")]
    [InlineData("dds.format=b8g8r8a8-unorm-srgb")]
    [InlineData("dds.format=b5g6r5-unorm")]
    [InlineData("dds.format=b5g5r5a1-unorm")]
    [InlineData("dds.format=b4g4r4a4-unorm")]
    [InlineData("dds.format=r10g10b10a2-unorm")]
    [InlineData("dds.format=r16g16b16a16-unorm")]
    [InlineData("dds.format=r16g16b16a16-float")]
    [InlineData("dds.format=r32g32b32a32-float")]
    [InlineData("dds.format=r8-unorm")]
    [InlineData("dds.format=r8g8-unorm")]
    [InlineData("dds.format=r16-unorm")]
    [InlineData("dds.format=a8-unorm")]
    [InlineData("dds.format=r11g11b10-float")]
    [InlineData("dds.format=r9g9b9e5-sharedexp")]
    [InlineData("dds.format=legacy-l8")]
    [InlineData("dds.format=legacy-a8l8")]
    [InlineData("dds.format=legacy-a8")]
    [InlineData("dds.format=legacy-r5g6b5")]
    [InlineData("dds.format=fourcc-a16b16g16r16f")]
    [InlineData("dds.format=fourcc-a32b32g32r32f")]
    [InlineData("dds.header=dx10")]
    [InlineData("dds.header=legacy")]
    [InlineData("dds.mips")]
    [InlineData("dds.cube")]
    [InlineData("dds.cube.partial")]
    [InlineData("dds.array")]
    [InlineData("dds.volume")]
    [InlineData("dds.dimension=1d")]
    [InlineData("dds.oddSize")]
    [InlineData("dds.alphaMode=straight")]
    [InlineData("dds.alphaMode=opaque")]
    [InlineData("dds.alphaMode=custom")]
    [InlineData("dds.writer=texconv")]
    [InlineData("dds.writer=texassemble")]
    [InlineData("dds.writer=magick")]
    public void EveryFeatureHasValidFixtures(string feature)
    {
        Assert.Contains(GoldenCorpus.Default.GetIds(format: "dds", kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get), fixture => fixture.Entry.HasFeature(feature));
    }

    [Fact]
    public void RandomBlocksReachEveryBptcMode()
    {
        // The random BC7 blocks cycle through the 8 modes and the reserved low byte 0, the BC6H ones through the 14 modes
        // and the reserved mode 0x13: 36 blocks cover each at least twice
        var bc7 = GoldenCorpus.Default.Get("dds/random-bc7-unorm").ReadInput();
        var modes = new HashSet<int>();
        for (var offset = 148; offset < bc7.Length; offset += 16)
        {
            modes.Add(bc7[offset] == 0 ? 8 : System.Numerics.BitOperations.TrailingZeroCount(bc7[offset]));
        }

        Assert.Equal(Enumerable.Range(0, 9), modes.Order());

        var bc6 = GoldenCorpus.Default.Get("dds/random-bc6h-sf16").ReadInput();
        var bc6Modes = new HashSet<int>();
        for (var offset = 148; offset < bc6.Length; offset += 16)
        {
            var mode = bc6[offset] & 3;
            bc6Modes.Add(mode < 2 ? mode : bc6[offset] & 31);
        }

        Assert.HasCount(15, bc6Modes);
    }

    [Theory]
    [MemberData(nameof(SurfaceIds))]
    public void EverySurfaceMatchesItsReference(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        using var collection = ImageCollection.Load(fixture.ReadInput());
        Assert.Equal(ImageFormat.Dds, collection.Format);
        Assert.Equal(ImageCollectionKind.TextureSurfaces, collection.Kind);
        Assert.Equal(fixture.DdsSurfaces.Count, collection.Count);
        for (var i = 0; i < collection.Count; i++)
        {
            var expected = fixture.DdsSurfaces[i];
            var entry = collection[i];
            var surface = entry.TextureSurface!;
            Assert.Equal(new Size(expected.Width, expected.Height), entry.Size);
            Assert.Equal(expected.ArrayIndex, surface.ArrayIndex);
            Assert.Equal(expected.Face, surface.Face?.ToString());
            Assert.Equal(expected.MipLevel, surface.MipLevel);
            Assert.Equal(expected.DepthSlice, surface.DepthSlice);
            Assert.Equal(fixture.Expected.PixelFormat, entry.PixelFormat.ToString());

            var reference = fixture.GetDdsSurfaceReference(expected).Memory;
            var actual = DecodeBytes(entry, expected.Buffer.Layout);
            Assert.True(reference.Span.SequenceEqual(actual), $"{id}: surface {i} ({surface}) differs from its reference.");
        }
    }

    [Theory]
    [MemberData(nameof(ValidIds))]
    public void IdentifyCountsTheSurfaces(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var info = Image.Identify(fixture.ReadInput());
        Assert.Equal(fixture.Entry.DdsSurfaces?.Count ?? 1, info.CollectionEntryCount);
    }

    [Theory]
    [InlineData("dds/r8g8b8a8-unorm-srgb", true, false)]
    [InlineData("dds/random-bc7-unorm-srgb", true, false)]
    [InlineData("dds/r8g8b8a8-unorm", false, false)]
    [InlineData("dds/random-bc6h-uf16", false, true)]
    [InlineData("dds/r16g16b16a16-float", false, true)]
    [InlineData("dds/random-bc5s", false, true)]
    public void FormatsLabelTheirColors(string id, bool isSrgb, bool isLinear)
    {
        using var image = Image.Load(GoldenCorpus.Default.Get(id).ReadInput());
        Assert.Equal(isSrgb, image.Metadata.Cicp is { ColorPrimaries: 1, TransferCharacteristics: 13 });
        Assert.Equal(isLinear ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
    }

    [Theory]
    [InlineData("invalid/dds/dxt2", "Premultiplied alpha")]
    [InlineData("invalid/dds/dxt4", "Premultiplied alpha")]
    [InlineData("invalid/dds/premultiplied-alpha", "Premultiplied alpha")]
    [InlineData("invalid/dds/yuy2", "Video format")]
    [InlineData("invalid/dds/depth-d24-s8", "DXGI format 45")]
    [InlineData("invalid/dds/video-nv12", "DXGI format 103")]
    public void UnsupportedFormatsAreRejectedWithTheirFeature(string id, string feature)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.Equal(FixtureKinds.Unsupported, fixture.Entry.Kind);
        Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(fixture.ReadInput())).Feature);
    }

    [Theory]
    [InlineData("invalid/dds/pitch-wrong")]
    [InlineData("invalid/dds/linear-size-wrong")]
    public void WrongPitchesAreIgnoredByLenientDecoding(string id)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        using var image = Image.Load(data, new ImageDecodeOptions { Configuration = lenient });

        // The pixels are those of the same file without DDSD_PITCH (0x8) and DDSD_LINEARSIZE (0x80000) in dwFlags
        var withoutField = data.ToArray();
        withoutField[8] &= 0xF7;
        withoutField[10] &= 0xF7;
        using var reference = Image.Load(withoutField);
        Assert.Equal(reference.Size, image.Size);
        Assert.Equal(GetBytes(reference), GetBytes(image));
    }

    private static byte[] DecodeBytes(ImageCollectionEntry entry, string layout)
    {
        switch (layout)
        {
            case "rgba8":
            {
                using var image = entry.Decode<Rgba32>();
                return GetBytes(image);
            }

            case "rgb8":
            {
                using var image = entry.Decode<Rgb24>();
                return GetBytes(image);
            }

            case "gray8":
            {
                using var image = entry.Decode<Gray8>();
                return GetBytes(image);
            }

            case "rgba16le":
            {
                using var image = entry.Decode<Rgba64>();
                return GetBytes(image);
            }

            case "gray16le":
            {
                using var image = entry.Decode<Gray16>();
                return GetBytes(image);
            }

            default:
            {
                using var image = entry.Decode<RgbaFloat>();
                return GetBytes(image);
            }
        }
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
