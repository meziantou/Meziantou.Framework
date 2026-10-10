using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// ICO and CUR decoding and encoding through the public APIs, on files assembled byte by byte by
/// <see cref="IcoFileBuilder"/> with literal expected pixels: the directory and its checked offsets, PNG-backed and
/// DIB-backed representations, the doubled DIB height, the AND mask and the implicit alpha of a 32-bit payload, cursor
/// hotspots, the selection rules, and the malformed files that must be rejected.
/// </summary>
public sealed class IcoCodecTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new IcoEncoder();
        Assert.Equal(ImageFormat.Ico, encoder.Format);
        Assert.Equal(IconKind.Icon, encoder.Kind);
        Assert.Equal(IconPayloadFormat.Auto, encoder.PayloadFormat);
        Assert.Equal(ImageFormat.Cur, new IcoEncoder { Kind = IconKind.Cursor }.Format);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IcoEncoder { Kind = (IconKind)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new IcoEncoder { PayloadFormat = (IconPayloadFormat)42 });
    }

    [Fact]
    public void IconsAndCursorsAreDetectedAndDistinguishedFromTga()
    {
        Assert.Equal(ImageFormat.Ico, Image.DetectFormat(SingleIcon()));
        Assert.Equal(ImageFormat.Cur, Image.DetectFormat(SingleCursor(hotspotX: 1, hotspotY: 1)));

        // A directory with no entry is not an icon; a TGA without a color map has zeros where the count is
        var empty = SingleIcon();
        BinaryPrimitives.WriteUInt16LittleEndian(empty.AsSpan(4), 0);
        Assert.NotEqual(ImageFormat.Ico, Image.DetectFormat(empty));
    }

    [Fact]
    public void ADibRepresentationUsesItsAlphaChannelWhenItHasOne()
    {
        Rgba32[] pixels = [new Rgba32(10, 20, 30, 255), new Rgba32(40, 50, 60, 128), new Rgba32(70, 80, 90, 0), new Rgba32(1, 2, 3, 200)];
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 2,
            Payload = IcoFileBuilder.Dib32(2, 2, pixels),
        }).Build();

        using var image = Image.Load(data);
        Assert.Equal(ImageFormat.Ico, image.Metadata.SourceFormat);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        Assert.Equal(pixels, Pixels<Rgba32>(data));
    }

    [Fact]
    public void AnAllZeroAlphaChannelIsUnusedAndTheAndMaskDecidesTransparency()
    {
        // Old 32-bit icons leave the fourth byte at zero; reading it as alpha would make the whole icon invisible
        Rgba32[] stored = [new Rgba32(10, 20, 30, 0), new Rgba32(40, 50, 60, 0)];
        bool[] mask = [false, true];
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            Payload = IcoFileBuilder.Dib32(2, 1, stored, mask),
        }).Build();

        Assert.Equal([new Rgba32(10, 20, 30, 255), new Rgba32(40, 50, 60, 0)], Pixels<Rgba32>(data));
    }

    [Fact]
    public void TheAndMaskOfALowDepthRepresentationBecomesAlpha()
    {
        Rgb24[] pixels = [new Rgb24(10, 20, 30), new Rgb24(40, 50, 60)];
        bool[] mask = [true, false];
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            BitCountOrHotspotY = 24,
            Payload = IcoFileBuilder.Dib24(2, 1, pixels, mask),
        }).Build();

        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        Assert.Equal([new Rgba32(10, 20, 30, 0), new Rgba32(40, 50, 60, 255)], Pixels<Rgba32>(data));
    }

    [Fact]
    public void APaletteRepresentationIsExpandedThroughItsPalette()
    {
        Rgb24[] palette = [new Rgb24(1, 2, 3), new Rgb24(200, 150, 100)];
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 3,
            Height = 1,
            ColorCount = 2,
            BitCountOrHotspotY = 8,
            Payload = IcoFileBuilder.Dib8(3, 1, [1, 0, 1], palette),
        }).Build();

        Assert.Equal(
            [new Rgba32(200, 150, 100), new Rgba32(1, 2, 3), new Rgba32(200, 150, 100)],
            Pixels<Rgba32>(data));
    }

    [Fact]
    public void RowsOfADibRepresentationAreStoredBottomUp()
    {
        Rgba32[] pixels = [new Rgba32(1, 1, 1), new Rgba32(2, 2, 2), new Rgba32(3, 3, 3), new Rgba32(4, 4, 4)];
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 2,
            Payload = IcoFileBuilder.Dib32(2, 2, pixels),
        }).Build();

        // The builder stores them bottom-up; the decoder must put them back in display order
        Assert.Equal(pixels, Pixels<Rgba32>(data));
    }

    [Fact]
    public void APngRepresentationIsDecodedByThePngCodec()
    {
        var png = SyntheticImages.Png(4, 4);
        var data = new IcoFileBuilder().Add(new IcoEntrySpec { Width = 4, Height = 4, Payload = png }).Build();

        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageFormat.Png, collection[0].PayloadFormat);
        Assert.Equal(new Size(4, 4), collection[0].Size);

        using var direct = Image.Load(png);
        using var fromIcon = collection[0].Decode();
        Assert.Equal(ImageFormat.Ico, fromIcon.Metadata.SourceFormat);
        TiffEncoderTests.AssertSamePixels((Image<Rgba32>)direct, (Image<Rgba32>)fromIcon);
    }

    [Fact]
    public void AnAnimatedPngPayloadIsRejected()
    {
        var apng = SyntheticImages.Apng(4, 4, frames: 2);
        var data = new IcoFileBuilder().Add(new IcoEntrySpec { Width = 4, Height = 4, Payload = apng }).Build();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Contains("animat", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepresentationsKeepTheirOwnSizeAndTheDirectoryIsNeverAnimation()
    {
        var data = ThreeSizes();
        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageCollectionKind.Representations, collection.Kind);
        Assert.Equal(ImageFormat.Ico, collection.Format);
        Assert.Equal(3, collection.Count);
        Assert.Equal(new Size(1, 1), collection[0].Size);
        Assert.Equal(new Size(4, 4), collection[1].Size);
        Assert.Equal(new Size(2, 2), collection[2].Size);

        foreach (var entry in collection.Entries)
        {
            using var image = entry.Decode();
            Assert.Equal(1, image.Frames.Count);
            Assert.False(image.IsAnimated);
            Assert.Null(image.Animation);
            Assert.Null(image.PosterFrame);
        }

        var info = Image.Identify(data);
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.Equal(3, info.CollectionEntryCount);
    }

    [Fact]
    public void LoadAndIdentifySelectTheLargestRepresentation()
    {
        var data = ThreeSizes();
        using var image = Image.Load(data);
        Assert.Equal(new Size(4, 4), image.Size);
        Assert.Equal(new Size(4, 4), Image.Identify(data).Size);
    }

    [Fact]
    public void SelectBySizePicksTheSmallestRepresentationThatIsLargeEnough()
    {
        using var collection = ImageCollection.Load(ThreeSizes());
        Assert.Equal(new Size(2, 2), collection.SelectBySize(new Size(2, 2)).Size);
        Assert.Equal(new Size(4, 4), collection.SelectBySize(new Size(3, 3)).Size);
        Assert.Equal(new Size(1, 1), collection.SelectBySize(new Size(1, 1)).Size);

        // Nothing is large enough: the largest representation is selected, and nothing is resized
        Assert.Equal(new Size(4, 4), collection.SelectBySize(new Size(64, 64)).Size);
    }

    [Fact]
    public void CursorHotspotsArePreservedAndValidatedAgainstTheirOwnRepresentation()
    {
        var data = SingleCursor(hotspotX: 1, hotspotY: 0);
        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageFormat.Cur, collection.Format);
        Assert.Equal(new Point(1, 0), collection[0].Hotspot);

        // An icon entry stores color planes and a bit count there, not a hotspot
        using var icon = ImageCollection.Load(SingleIcon());
        Assert.Null(icon[0].Hotspot);

        var outside = SingleCursor(hotspotX: 4, hotspotY: 0);
        var exception = Assert.Throws<InvalidImageContentException>(() => ImageCollection.Load(outside.AsSpan()));
        Assert.Contains("hotspot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedDirectoriesAreRejected()
    {
        // A payload that starts inside the directory
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            OffsetOverride = 4,
            Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
        }).Build()));

        // A payload that runs past the end of the file
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            LengthOverride = 1_000_000,
            Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
        }).Build()));

        // An empty payload, after a valid one
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new IcoFileBuilder()
            .Add(new IcoEntrySpec { Width = 2, Height = 1, Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]) })
            .Add(new IcoEntrySpec
            {
                Width = 2,
                Height = 1,
                LengthOverride = 0,
                Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
            }).Build()));

        // The detection prefix already requires a non-empty first payload, so a file whose only entry is empty is not
        // recognized as an icon at all: the four signature bytes of an icon are too weak on their own
        Assert.Throws<UnknownImageFormatException>(() => Image.Load(new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            LengthOverride = 0,
            Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
        }).Build()));

        // A non-zero reserved byte, after a valid entry (the detection prefix already checks the first one)
        Assert.Throws<InvalidImageContentException>(() => Image.Load(new IcoFileBuilder()
            .Add(new IcoEntrySpec { Width = 2, Height = 1, Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]) })
            .Add(new IcoEntrySpec
            {
                Width = 2,
                Height = 1,
                Reserved = 7,
                Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
            }).Build()));
    }

    [Fact]
    public void ADibPayloadMustDeclareTheDoubledHeightOfAnIcon()
    {
        var rows = new byte[1][];
        rows[0] = new byte[8];
        var odd = IcoFileBuilder.Dib(2, 1, 32, palette: null, rows, mask: null, includeMask: true, storedHeightOverride: 3);
        var data = new IcoFileBuilder().Add(new IcoEntrySpec { Width = 2, Height = 1, Payload = odd }).Build();
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        Assert.Contains("doubled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADirectoryDimensionThatContradictsThePayloadIsRejected()
    {
        var data = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 2,
            Height = 1,
            DeclaredWidth = 8,
            Payload = IcoFileBuilder.Dib32(2, 1, [new Rgba32(1, 2, 3), new Rgba32(4, 5, 6)]),
        }).Build();

        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));

        // A 256-pixel side is stored as 0, and a payload of a different size next to a 0 byte is accepted
        var png = SyntheticImages.Png(4, 4);
        var tolerated = new IcoFileBuilder().Add(new IcoEntrySpec
        {
            Width = 4,
            Height = 4,
            DeclaredWidth = 0,
            DeclaredHeight = 0,
            Payload = png,
        }).Build();

        using var image = Image.Load(tolerated);
        Assert.Equal(new Size(4, 4), image.Size);
    }

    /// <summary>The inputs an icon is buffered whole from: every one that is not a synchronous read of a seekable source.</summary>
    public static TheoryData<InputVariant> BufferedVariants =>
        [InputVariant.Span, InputVariant.NonSeekableStream, InputVariant.ShortReadStream, InputVariant.AsyncPath, InputVariant.AsyncStream, InputVariant.AsyncShortReadStream];

    [Theory]
    [MemberData(nameof(BufferedVariants))]
    public async Task ABufferedInputNeedsExactlyItsLengthInEncodedBytes(InputVariant variant)
    {
        // The whole input is buffered up to its end, which a stream only reports once it is read past
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, SingleIcon(), ImageFormat.Ico, XunitCancellationToken);
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, SingleCursor(1, 1), ImageFormat.Cur, XunitCancellationToken);
        await InputVariants.AssertEncodedByteLimitBoundaryAsync(variant, ThreeSizes(), ImageFormat.Ico, XunitCancellationToken);
    }

    [Fact]
    public void ASequentialReaderRejectsIconsExplicitly()
    {
        using var stream = new MemoryStream(SingleIcon());
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<Rgba32>(stream));
        Assert.Contains("ImageCollection", exception.Message, StringComparison.Ordinal);
    }

    private static byte[] SingleIcon() => new IcoFileBuilder().Add(new IcoEntrySpec
    {
        Width = 2,
        Height = 2,
        Payload = IcoFileBuilder.Dib32(2, 2, [new Rgba32(1, 2, 3, 255), new Rgba32(4, 5, 6, 255), new Rgba32(7, 8, 9, 255), new Rgba32(10, 11, 12, 255)]),
    }).Build();

    private static byte[] SingleCursor(int hotspotX, int hotspotY) => new IcoFileBuilder { Type = 2 }.Add(new IcoEntrySpec
    {
        Width = 2,
        Height = 2,
        PlanesOrHotspotX = (ushort)hotspotX,
        BitCountOrHotspotY = (ushort)hotspotY,
        Payload = IcoFileBuilder.Dib32(2, 2, [new Rgba32(1, 2, 3, 255), new Rgba32(4, 5, 6, 255), new Rgba32(7, 8, 9, 255), new Rgba32(10, 11, 12, 255)]),
    }).Build();

    private static byte[] ThreeSizes() => new IcoFileBuilder()
        .Add(new IcoEntrySpec { Width = 1, Height = 1, Payload = IcoFileBuilder.Dib32(1, 1, [new Rgba32(1, 1, 1, 255)]) })
        .Add(new IcoEntrySpec { Width = 4, Height = 4, Payload = IcoFileBuilder.Dib32(4, 4, [.. Enumerable.Range(0, 16).Select(static i => new Rgba32((byte)i, (byte)i, (byte)i, 255))]) })
        .Add(new IcoEntrySpec { Width = 2, Height = 2, Payload = IcoFileBuilder.Dib32(2, 2, [.. Enumerable.Range(0, 4).Select(static i => new Rgba32((byte)i, 0, 0, 255))]) })
        .Build();

    private static TPixel[] Pixels<TPixel>(byte[] data)
        where TPixel : unmanaged
    {
        using var image = Image.Load<TPixel>(data);
        var pixels = new TPixel[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }
}
