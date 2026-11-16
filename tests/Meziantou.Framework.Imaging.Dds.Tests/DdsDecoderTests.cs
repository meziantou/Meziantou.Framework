using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dds;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DDS decoding of files assembled by <see cref="DdsFileBuilder"/> (the DDS reference of Microsoft Learn): the headers, the
/// surfaces of mipmaps, cube maps, arrays and volume textures and their entries in <see cref="ImageCollection"/>, the
/// limits, the truncations and lenient decoding. The expected pixels are those of <see cref="ReferenceDds"/>.
/// </summary>
public sealed class DdsDecoderTests
{
    [Fact]
    public void UncompressedSurfaceLoadsAndIdentifies()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        var data = DdsFileBuilder.Dx10(3, 1, 28, pixels).Build();
        Assert.Equal(ImageFormat.Dds, Image.DetectFormat(data));
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Dds, info.Format);
        Assert.Equal(new Size(3, 1), info.Size);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgba, info.ColorModel);
        Assert.Equal(8, info.BitsPerComponent);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal(1, info.CollectionEntryCount);
        Assert.Equal(true, info.MayHaveTransparency);

        using var image = Assert.IsType<Image<Rgba32>>(Image.Load(data));
        Assert.Equal(new Rgba32(1, 2, 3, 4), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(9, 10, 11, 12), image.Frames[0][2, 0]);
        Assert.Equal(ImageFormat.Dds, image.Metadata.SourceFormat);
        Assert.Null(image.Metadata.Cicp);
        Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
    }

    [Fact]
    public void MipmapsAreEntriesOfACollection()
    {
        // 5x3, 2x1 and 1x1 levels of R8 texels
        byte[] levels = [.. Enumerable.Range(0, 15).Select(i => (byte)i), 100, 101, 200];
        var builder = DdsFileBuilder.Dx10(5, 3, 61, levels);
        builder.MipMapCount = 3;
        var data = builder.Build();

        using (var top = Image.Load<Gray8>(data))
        {
            Assert.Equal(new Size(5, 3), top.Size);
            Assert.Equal(new Gray8(14), top.Frames[0][4, 2]);
        }

        Assert.Equal(3, Image.Identify(data).CollectionEntryCount);
        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageCollectionKind.TextureSurfaces, collection.Kind);
        Assert.Equal(ImageFormat.Dds, collection.Format);
        Assert.Equal(new[] { new Size(5, 3), new Size(2, 1), new Size(1, 1) }, collection.Entries.Select(entry => entry.Size).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, collection.Entries.Select(entry => entry.TextureSurface!.MipLevel).ToArray());
        Assert.All(collection.Entries, entry => entry.TextureSurface is { ArrayIndex: 0, Face: null, DepthSlice: 0 });
        Assert.All(collection.Entries, entry => entry.PixelFormat == PixelFormat.Gray8 && entry.PayloadFormat == ImageFormat.Dds);

        using var level1 = collection[1].Decode<Gray8>();
        Assert.Equal(new Gray8(100), level1.Frames[0][0, 0]);
        Assert.Equal(new Gray8(101), level1.Frames[0][1, 0]);
        using var level2 = collection[2].Decode();
        Assert.Equal(new Size(1, 1), level2.Size);
        Assert.Equal(new Gray8(200), ((Image<Gray8>)level2).Frames[0][0, 0]);
    }

    [Fact]
    public void CubeMapArraysListEveryFaceOfEveryElement()
    {
        // Two cube maps of 1x1 faces, one level, A8 texels whose values are their index in the file
        var builder = DdsFileBuilder.Dx10(1, 1, 65, Enumerable.Range(0, 12).Select(i => (byte)(i * 10)).ToArray());
        builder.MiscFlag = 4;
        builder.ArraySize = 2;
        using var collection = ImageCollection.Load(builder.Build());
        Assert.Equal(12, collection.Count);
        for (var i = 0; i < 12; i++)
        {
            var surface = collection[i].TextureSurface!;
            Assert.Equal(i / 6, surface.ArrayIndex);
            Assert.Equal((CubeMapFace)(i % 6), surface.Face);
            Assert.Equal(0, surface.MipLevel);
            using var image = collection[i].Decode<A8>();
            Assert.Equal(new A8((byte)(i * 10)), image.Frames[0][0, 0]);
        }
    }

    [Fact]
    public void PartialCubeMapsListTheirStoredFacesInOrder()
    {
        // Legacy cube map of the faces -X, +Y and +Z (DDSCAPS2_CUBEMAP_NEGATIVEX, POSITIVEY, POSITIVEZ), 2x2 then 1x1 levels
        byte[] data = [.. Enumerable.Range(0, 15).Select(i => (byte)i)];
        var builder = DdsFileBuilder.Masks(2, 2, DdsFileBuilder.PixelLuminance, 8, 0xFF, 0, 0, 0, data);
        builder.Caps2 = DdsFileBuilder.Caps2CubeMap | 0x800 | 0x1000 | 0x4000;
        builder.MipMapCount = 2;
        using var collection = ImageCollection.Load(builder.Build());
        Assert.Equal(
            new[] { (CubeMapFace.NegativeX, 0), (CubeMapFace.NegativeX, 1), (CubeMapFace.PositiveY, 0), (CubeMapFace.PositiveY, 1), (CubeMapFace.PositiveZ, 0), (CubeMapFace.PositiveZ, 1) },
            collection.Entries.Select(entry => (entry.TextureSurface!.Face!.Value, entry.TextureSurface.MipLevel)).ToArray());
        using var lastFace = collection[5].Decode<Gray8>();
        Assert.Equal(new Gray8(14), lastFace.Frames[0][0, 0]);
        Assert.Equal("array 0, face PositiveZ, mip 1, slice 0", collection[5].TextureSurface!.ToString());
    }

    [Fact]
    public void VolumeTexturesListTheDepthSlicesOfEveryLevel()
    {
        // 2x2x3, then 1x1x1: the slices of a level are stored one after the other
        var builder = DdsFileBuilder.Dx10(2, 2, 61, Enumerable.Range(0, 13).Select(i => (byte)i).ToArray());
        builder.ResourceDimension = 4;
        builder.Depth = 3;
        builder.MipMapCount = 2;
        var data = builder.Build();
        using var collection = ImageCollection.Load(data);
        Assert.Equal(new[] { (0, 0), (0, 1), (0, 2), (1, 0) }, collection.Entries.Select(entry => (entry.TextureSurface!.MipLevel, entry.TextureSurface.DepthSlice)).ToArray());
        using var slice2 = collection[2].Decode<Gray8>();
        Assert.Equal(new Gray8(8), slice2.Frames[0][0, 0]);

        // A load gives the first slice of the top level
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(new Gray8(3), image.Frames[0][1, 1]);
    }

    [Fact]
    public void BlockCompressedSurfacesOfOddSizesKeepTheirSize()
    {
        // A 5x2 BC4 surface is two blocks wide and one block high; its mipmaps are 2x1 and 1x1, a block each
        var blocks = new byte[(2 + 1 + 1) * 8];
        blocks[0] = 7;
        blocks[1] = 7;
        blocks[8] = 9;
        blocks[9] = 9;
        blocks[24] = 50;
        blocks[25] = 50;
        var builder = DdsFileBuilder.Legacy(5, 2, DdsFileBuilder.FourCC("ATI1"), blocks);
        builder.MipMapCount = 3;
        using var collection = ImageCollection.Load(builder.Build());
        using var top = collection[0].Decode<Gray8>();
        Assert.Equal(new Size(5, 2), top.Size);
        Assert.Equal(new Gray8(7), top.Frames[0][3, 1]);
        Assert.Equal(new Gray8(9), top.Frames[0][4, 1]);
        using var last = collection[2].Decode<Gray8>();
        Assert.Equal(new Gray8(50), last.Frames[0][0, 0]);
    }

    [Fact]
    public void TruncatedLaterSurfacesAreFoundByTheCollection()
    {
        // The top level is complete, the second one is cut: a load reads the top level only
        var builder = DdsFileBuilder.Dx10(2, 2, 61, new byte[4 + 1]);
        builder.MipMapCount = 2;
        var data = builder.Build()[..^1];
        using (var image = Image.Load(data))
        {
            Assert.Equal(new Size(2, 2), image.Size);
        }

        Assert.Equal(2, Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).CollectionEntryCount);
        Assert.Throws<InvalidImageContentException>(() => ImageCollection.Load(data));
    }

    [Fact]
    public void SurfacesCountAgainstTheFrameLimit()
    {
        var builder = DdsFileBuilder.Dx10(1, 1, 61, new byte[6]);
        builder.MiscFlag = 4;
        var data = builder.Build();
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 5 } };
        Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => ImageCollection.Load(data, configuration)).Kind);
        using var collection = ImageCollection.Load(data, new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 6 } });
        Assert.Equal(6, collection.Count);
    }

    [Fact]
    public void HugeArraysAreRejectedBeforeAnythingIsAllocated()
    {
        // 4 billion elements of 1x1 R32G32B32A32_FLOAT: the size of the data cannot be represented
        var builder = DdsFileBuilder.Dx10(1, 1, 2, new byte[16]);
        builder.ArraySize = uint.MaxValue;
        builder.MiscFlag = 4;
        builder.MipMapCount = 1;
        var data = builder.Build();
        using (var image = Image.Load(data))
        {
            Assert.Equal(new Size(1, 1), image.Size);
        }

        Assert.Null(Image.Identify(data).CollectionEntryCount);
        Assert.Throws<InvalidImageContentException>(() => ImageCollection.Load(data));
    }

    [Fact]
    public void WrongPitchIsRejectedUnlessDecodingIsLenient()
    {
        var builder = DdsFileBuilder.Dx10(3, 2, 61, new byte[] { 1, 2, 3, 4, 5, 6 });
        builder.Flags |= DdsFileBuilder.FlagPitch;
        builder.PitchOrLinearSize = 4;
        var data = builder.Build();
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Throws<InvalidImageContentException>(() => ImageCollection.Load(data));

        var lenient = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
        using var image = Image.Load<Gray8>(data, new ImageDecodeOptions { Configuration = lenient });
        Assert.Equal(new Gray8(4), image.Frames[0][0, 1]);
        using var collection = ImageCollection.Load(data, lenient);
        Assert.Equal(1, collection.Count);

        // The pitch of the rows, the length of the top surface, or a field without its flag, are accepted in strict mode
        builder.PitchOrLinearSize = 3;
        Image.Load(builder.Build()).Dispose();
        builder.Flags &= ~DdsFileBuilder.FlagPitch;
        builder.PitchOrLinearSize = 1234;
        Image.Load(builder.Build()).Dispose();
    }

    [Theory]
    [InlineData(29u)]
    [InlineData(99u)]
    public void SrgbFormatsAreLabeledWithTheCicpDescriptionOfSrgb(uint format)
    {
        var data = DdsFileBuilder.Dx10(4, 4, format, new byte[64]).Build();
        using var image = Image.Load(data);
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), image.Metadata.Cicp);
        Assert.Equal(new CicpColorDescription(1, 13, 0, fullRange: true), Image.Identify(data).Metadata.Cicp);
    }

    [Fact]
    public void FloatingPointFormatsAreLinearAndKeepValuesOutsideTheUnitRange()
    {
        var pixels = new byte[16];
        BitConverter.TryWriteBytes(pixels.AsSpan(0), 2.5f);
        BitConverter.TryWriteBytes(pixels.AsSpan(4), -0.25f);
        BitConverter.TryWriteBytes(pixels.AsSpan(8), 1e-3f);
        BitConverter.TryWriteBytes(pixels.AsSpan(12), 0.5f);
        using var image = Assert.IsType<Image<RgbaFloat>>(Image.Load(DdsFileBuilder.Dx10(1, 1, 2, pixels).Build()));
        Assert.Equal(new RgbaFloat(2.5f, -0.25f, 1e-3f, 0.5f), image.Frames[0][0, 0]);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
    }

    [Fact]
    public void SingleChannelFloatingPointFormatsDecodeToGray()
    {
        var floats = new byte[8];
        BitConverter.TryWriteBytes(floats.AsSpan(0), 2.5f);
        BitConverter.TryWriteBytes(floats.AsSpan(4), -0.25f);
        using (var image = Assert.IsType<Image<GrayFloat>>(Image.Load(DdsFileBuilder.Dx10(2, 1, 41, floats).Build())))
        {
            Assert.Equal(new GrayFloat(2.5f), image.Frames[0][0, 0]);
            Assert.Equal(new GrayFloat(-0.25f), image.Frames[0][1, 0]);
        }

        var halves = new byte[4];
        BitConverter.TryWriteBytes(halves.AsSpan(0), (Half)2.5f);
        BitConverter.TryWriteBytes(halves.AsSpan(2), (Half)(-0.25f));
        using (var image = Assert.IsType<Image<GrayHalf>>(Image.Load(DdsFileBuilder.Dx10(2, 1, 54, halves).Build())))
        {
            Assert.Equal(new GrayHalf((Half)2.5f), image.Frames[0][0, 0]);
            Assert.Equal(new GrayHalf((Half)(-0.25f)), image.Frames[0][1, 0]);
        }

        var info = Image.Identify(DdsFileBuilder.Dx10(2, 1, 41, floats).Build());
        Assert.Equal(PixelFormat.GrayFloat, info.PixelFormat);
        Assert.Equal(ImageColorModel.Grayscale, info.ColorModel);
        Assert.Equal(32, info.BitsPerComponent);
    }

    [Fact]
    public void AlphaModesOtherThanPremultipliedKeepTheStoredAlpha()
    {
        foreach (var mode in new uint[] { 0, 1, 3, 4 })
        {
            var builder = DdsFileBuilder.Dx10(1, 1, 28, new byte[] { 10, 20, 30, 40 });
            builder.MiscFlags2 = mode;
            using var image = Image.Load<Rgba32>(builder.Build());
            Assert.Equal(new Rgba32(10, 20, 30, 40), image.Frames[0][0, 0]);
        }

        var premultiplied = DdsFileBuilder.Dx10(1, 1, 28, new byte[] { 10, 20, 30, 40 });
        premultiplied.MiscFlags2 = 2;
        Assert.Equal("Premultiplied alpha", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(premultiplied.Build())).Feature);
    }

    [Fact]
    public void TypelessFormatsWithAnUnormReadingAreRead()
    {
        // R8G8B8A8_TYPELESS reads as R8G8B8A8_UNORM; BC6H_TYPELESS (unsigned or signed) is rejected
        using var image = Image.Load<Rgba32>(DdsFileBuilder.Dx10(1, 1, 27, new byte[] { 1, 2, 3, 4 }).Build());
        Assert.Equal(new Rgba32(1, 2, 3, 4), image.Frames[0][0, 0]);
        Assert.Equal("DXGI format 94", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(DdsFileBuilder.Dx10(4, 4, 94, new byte[16]).Build())).Feature);
    }

    [Fact]
    public void DdsHasOneFileExtension()
    {
        Assert.Equal(new[] { ".dds" }, ImageFormatInfo.Get(ImageFormat.Dds).FileExtensions.ToArray());
    }
}
