using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.SnapshotTesting.Imaging.Tests;

public sealed class ImageSnapshotTests
{
    private static readonly Rgba32 White = Colors.White;
    private static readonly Rgba32 Black = Colors.Black;

    [Fact]
    public void ReadmeExample_AddImageComparer()
    {
        #region SnapshotTestingImaging_AddImageComparer
        var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
        settings.Comparers.AddImageComparer();
        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
        #endregion
    }

    // The verified frames are __snapshots__/ImageSnapshotTests_ReadmeExample_GifAndIcoFrames*.verified.png
    [Fact]
    public void ReadmeExample_GifAndIcoFrames()
    {
        #region SnapshotTestingImaging_GifAndIcoFrames
        var settings = SnapshotSettings.Default with { };
        settings.Serializers.AddGifSerializer();
        settings.Serializers.AddIcoSerializer();
        settings.Comparers.AddImageComparer();

        var gifBytes = File.ReadAllBytes("animation.gif");
        var icoBytes = File.ReadAllBytes("favicon.ico");
        Snapshot.Validate(gifBytes, SnapshotType.Gif, settings);
        Snapshot.Validate(icoBytes, SnapshotType.Ico, settings);
        #endregion
    }

    [Fact]
    public void ReadmeExample_Thresholds()
    {
        #region SnapshotTestingImaging_Thresholds
        var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
        settings.AddImaging(new ImageComparisonSettings
        {
            SimilarityThreshold = 0.99f, // SSIM: 0.0 = completely different, 1.0 = identical
            DHashThreshold = 4,          // dHash distance: 0 = same structure, up to 64
            PHashThreshold = 4,          // pHash distance: 0 = same structure, up to 64
        });
        #endregion

        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
    }

    [Fact]
    public void AddImaging_RegistersTheComparerForEveryImageType()
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();

        Assert.All(ImageTypes, type => Assert.IsType<ImageComparer>(settings.Comparers.Get(type)));
        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Create("jpg")));
        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Create("tif")));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    [InlineData("webp")]
    [InlineData("gif")]
    [InlineData("ico")]
    [InlineData("cur")]
    [InlineData("ani")]
    [InlineData("qoi")]
    [InlineData("tga")]
    [InlineData("pnm")]
    [InlineData("avif")]
    [InlineData("heic")]
    [InlineData("jxl")]
    [InlineData("jp2")]
    public void AddImaging_SerializesImagesWithoutChangingTheirPixels(string extension)
    {
        var type = SnapshotType.Create(extension);
        var settings = new SnapshotSettings();
        settings.AddImaging();

        // GIF stores transparency as on or off: the translucent quadrant would become opaque
        using var image = CreateQuadrants(16, 16, translucentAlpha: extension is "gif" ? (byte)255 : (byte)128);

        var snapshot = Assert.Single(settings.Serializers.Serialize(type, image).Data);

        Assert.Equal(type.FileExtension, snapshot.Extension);
        using var decoded = Image.Load<Rgba32>(snapshot.Data);
        Assert.Equal(GetPixels(image), GetPixels(decoded));
    }

    [Theory]
    [InlineData("png", true)]
    [InlineData("tiff", true)]
    [InlineData("pnm", true)]
    [InlineData("jxl", true)]
    [InlineData("jp2", true)]
    [InlineData("avif", false)]
    [InlineData("heic", false)]
    [InlineData("qoi", false)]
    [InlineData("tga", false)]
    public void AddImaging_SerializesSixteenBitImages(string extension, bool keepsSamples)
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();
        using var image = new Image<Rgba64>(16, 8, new Rgba64(0x8081, 0x4041, 0x2021, 0xFFFF));

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Create(extension), image).Data);

        using var decoded = Image.Load<Rgba64>(snapshot.Data);
        var pixel = decoded.Frames[0][4, 4];
        if (keepsSamples)
        {
            Assert.Equal(new Rgba64(0x8081, 0x4041, 0x2021, 0xFFFF), pixel);
        }
        else
        {
            Assert.InRange(pixel.R, 0x8000, 0x8100);
        }
    }

    [Fact]
    public void AddImaging_SerializesImagesAsJpeg()
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();
        using var image = new Image<Rgba64>(16, 8, new Rgba64(0x8080, 0x4040, 0x2020, 0xFFFF));

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Jpeg, image).Data);

        // A lossy format: the 16-bit samples are reduced to 8 bits, and the colors are close to the original ones
        Assert.Equal(ImageFormat.Jpeg, Image.DetectFormat(snapshot.Data));
        using var decoded = Image.Load<Rgba32>(snapshot.Data);
        Assert.Equal(new Size(16, 8), decoded.Size);
        Assert.InRange(decoded.Frames[0][4, 4].R, 0x7C, 0x84);
    }

    [Fact]
    public void AddImaging_KeepsTheFramesOfAnAnimation()
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();
        using var animation = CreateAnimation(White, Black);

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Gif, animation).Data);

        using var decoded = Image.Load<Rgba32>(snapshot.Data);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(Black, decoded.Frames[1][0, 0]);
    }

    [Fact]
    public void AddImaging_DoesNotSerializeOtherValuesOrTypes()
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();

        // A byte array is left to the default serializer
        byte[] bytes = [1, 2, 3];
        Assert.Equal(bytes, Assert.Single(settings.Serializers.Serialize(SnapshotType.Png, bytes).Data).Data);
        using var image = new Image<Rgba32>(1, 1);
        Assert.False(new ImageSnapshotSerializer().TrySerialize(SnapshotType.Svg, image, out _));
        Assert.False(new ImageSnapshotSerializer().TrySerialize(SnapshotType.Png, "text", out _));
    }

    [Fact]
    public void AddImaging_WritesColorsAsHexadecimalInHumanReadableSnapshots()
    {
        var settings = new SnapshotSettings();
        settings.AddImaging();

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Default, new { Color = new Rgba32(0x12, 0xAB, 0x00, 0x80) }).Data);

        Assert.Equal("Color: #12AB0080", Encoding.UTF8.GetString(snapshot.Data).Trim());
    }

    [Fact]
    public void AddImaging_ValidatesImagesEndToEnd()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateSettings(directory, "png", SnapshotUpdateStrategy.OverwriteWithoutFailure);
        settings.AddImaging();
        using (var image = CreateQuadrants(8, 8))
        {
            Snapshot.Validate(image, SnapshotType.Png, settings);
        }

        var disallow = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        using (var image = CreateQuadrants(8, 8))
        {
            Snapshot.Validate(image, SnapshotType.Png, disallow);
        }

        using var changed = CreateQuadrants(8, 8);
        changed.Frames[0][0, 0] = new Rgba32(1, 2, 3);
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(changed, SnapshotType.Png, disallow));
    }

    [Fact]
    public void AddGifSerializer_SerializesGifByteArrayAsOneSnapshotPerFrame()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();

        var data = settings.Serializers.Serialize(SnapshotType.Gif, CreateTwoFrameGif());

        Assert.HasCount(2, data.Data);
        Assert.All(data.Data, snapshot => Assert.Equal(SnapshotType.Png.FileExtension, snapshot.Extension));
        Assert.All(data.Data, snapshot => Assert.Equal([White], GetPixels(snapshot.Data)));
    }

    [Fact]
    public void AddGifSerializer_SerializesTheDisplayedFrames()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        using var animation = CreateAnimation(White, Black);

        var data = settings.Serializers.Serialize(SnapshotType.Gif, Encode(animation, new GifEncoder()));

        Assert.HasCount(2, data.Data);
        Assert.Equal(GetPixels(animation.Frames[0]), GetPixels(data.Data[0].Data));
        Assert.Equal(GetPixels(animation.Frames[1]), GetPixels(data.Data[1].Data));
    }

    [Theory]
    [InlineData("not-a-gif")]
    [InlineData("GIF89a-truncated")]
    public void AddGifSerializer_FallsBackToBinarySerializerWhenPayloadIsNotAGif(string text)
    {
        var payload = Encoding.ASCII.GetBytes(text);
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Gif, payload).Data);

        Assert.Equal(SnapshotType.Gif.FileExtension, snapshot.Extension);
        Assert.Equal(payload, snapshot.Data);
    }

    [Fact]
    public void AddIcoSerializer_SerializesIcoByteArrayAsOneSnapshotPerEntry()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.AddIcoSerializer();

        var data = settings.Serializers.Serialize(SnapshotType.Ico, CreateTwoEntryIco());

        Assert.HasCount(2, data.Data);
        Assert.All(data.Data, snapshot => Assert.Equal(SnapshotType.Png.FileExtension, snapshot.Extension));
        Assert.Equal(Enumerable.Repeat(White, 16 * 16), GetPixels(data.Data[0].Data));
        Assert.Equal(Enumerable.Repeat(Black, 32 * 32), GetPixels(data.Data[1].Data));
    }

    [Fact]
    public void AddIcoSerializer_FallsBackToBinarySerializerWhenPayloadIsNotAnIco()
    {
        var payload = "not-an-ico"u8.ToArray();
        var settings = new SnapshotSettings();
        settings.Serializers.AddIcoSerializer();

        var snapshot = Assert.Single(settings.Serializers.Serialize(SnapshotType.Ico, payload).Data);

        Assert.Equal(SnapshotType.Ico.FileExtension, snapshot.Extension);
        Assert.Equal(payload, snapshot.Data);
    }

    [Fact]
    public void AddGifAndIcoSerializers_RegisterTheSerializersOnlyOnce()
    {
        var settings = new SnapshotSettings();
        settings.Serializers.AddGifSerializer();
        settings.Serializers.AddIcoSerializer();
        var count = settings.Serializers.Count;

        settings.Serializers.AddGifSerializer();
        settings.Serializers.AddIcoSerializer();

        Assert.HasCount(count, settings.Serializers);
        Assert.Single(settings.Serializers, static serializer => serializer == FrameSnapshotSerializer.Gif);
        Assert.Single(settings.Serializers, static serializer => serializer == FrameSnapshotSerializer.Ico);
    }

    // The verified frames were written by another PNG encoder: they match because the frames are compared by pixels
    [Fact]
    public void AddGifSerializer_StaticFixture_Snapshot()
    {
        var settings = SnapshotSettings.Default with { };
        settings.Serializers.AddGifSerializer();
        settings.Comparers.AddImageComparer();

        Snapshot.Validate(ReadFixture("serializer-two-frame.gif"), SnapshotType.Gif, settings);
    }

    [Fact]
    public void AddIcoSerializer_StaticFixture_Snapshot()
    {
        var settings = SnapshotSettings.Default with { };
        settings.Serializers.AddIcoSerializer();
        settings.Comparers.AddImageComparer();

        Snapshot.Validate(ReadFixture("serializer-multi-entry.ico"), SnapshotType.Ico, settings);
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("ico")]
    public void Validate_ComparesGifAndIcoFramesByPixels_WhenAnImageComparerIsRegistered(string format)
    {
        using var directory = TemporaryDirectory.Create();
        var snapshotType = SnapshotType.Create(format);
        var payload = format is "gif" ? CreateTwoFrameGif() : CreateTwoEntryIco();
        var settings = CreateSettings(directory, "png", SnapshotUpdateStrategy.OverwriteWithoutFailure);
        settings.Serializers.AddGifSerializer();
        settings.Serializers.AddIcoSerializer();
        settings.Comparers.AddImageComparer();
        Snapshot.Validate(payload, snapshotType, settings);

        // Rewrite each frame with the same pixels but stored deflate blocks, as another zlib could compress them
        var framePaths = Directory.GetFiles(directory.FullPath, "*.verified.png");
        Assert.HasCount(2, framePaths);
        foreach (var framePath in framePaths)
        {
            var encodedFrame = File.ReadAllBytes(framePath);
            using var frame = Image.Load(encodedFrame);
            var storedFrame = Encode(frame, new PngEncoder { CompressionLevel = CompressionLevel.NoCompression });
            Assert.NotEqual(encodedFrame, storedFrame);
            File.WriteAllBytes(framePath, storedFrame);
        }

        var disallow = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        Snapshot.Validate(payload, snapshotType, disallow);

        // Without an image comparer, the frames are compared byte for byte
        var byteSettings = disallow with { };
        byteSettings.Comparers.Remove(SnapshotType.Png);
        byteSettings.Comparers.Remove(snapshotType);
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(payload, snapshotType, byteSettings));
    }

    [Fact]
    public void AddImageComparer_ComparesBmpSnapshotsByPixels()
    {
        using var image = new Image<Rgb24>(1, 1, new Rgb24(1, 2, 3));
        image.Metadata.Resolution = new ImageResolution(72, 72);
        var expected = Encode(image, new BmpEncoder());
        image.Metadata.Resolution = new ImageResolution(96, 96);
        var actual = Encode(image, new BmpEncoder { RowOrder = BmpRowOrder.TopDown });
        Assert.NotEqual(expected, actual);

        var comparer = CreateComparer(SnapshotType.Bmp);
        Assert.True(comparer.Equals(new SnapshotData("bmp", expected), new SnapshotData("bmp", actual)));

        image.Frames[0][0, 0] = new Rgb24(4, 5, 6);
        Assert.False(comparer.Equals(new SnapshotData("bmp", expected), new SnapshotData("bmp", Encode(image, new BmpEncoder()))));
    }

    [Fact]
    public void AddImageComparer_ComparesPngSnapshotsByPixels()
    {
        using var image = CreateQuadrants(8, 8);
        var expected = Encode(image, new PngEncoder());
        var actual = Encode(image, new PngEncoder { Interlaced = true, Filter = PngFilter.None, CompressionLevel = CompressionLevel.NoCompression });
        Assert.NotEqual(expected, actual);

        var comparer = CreateComparer(SnapshotType.Png);
        Assert.True(comparer.Equals(new SnapshotData("png", expected), new SnapshotData("png", actual)));

        image.Frames[0][7, 7] = new Rgba32(4, 5, 6);
        Assert.False(comparer.Equals(new SnapshotData("png", expected), new SnapshotData("png", Encode(image, new PngEncoder()))));
    }

    [Theory]
    [InlineData("jpeg")]
    [InlineData("jpg")]
    public void AddImageComparer_ComparesJpegSnapshotsByPixels(string extension)
    {
        using var image = CreateQuadrants(16, 16, translucentAlpha: 255);
        var expected = Encode(image, new JpegEncoder());
        var actual = AddJpegComment(expected, "metadata-only-difference");

        var comparer = CreateComparer(SnapshotType.Create(extension));
        Assert.True(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, actual)));

        image.Frames[0].ProcessPixelRows(pixels => pixels.GetRowSpan(0).Fill(new Rgba32(0, 128, 0)));
        Assert.False(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, Encode(image, new JpegEncoder()))));
    }

    [Theory]
    [InlineData("tiff")]
    [InlineData("tif")]
    public void AddImageComparer_ComparesTiffSnapshotsByPixels(string extension)
    {
        using var image = CreateQuadrants(8, 8);
        var expected = Encode(image, new TiffEncoder { Compression = TiffCompression.None });
        var actual = Encode(image, new TiffEncoder { Compression = TiffCompression.Lzw, BigEndian = true });

        var comparer = CreateComparer(SnapshotType.Create(extension));
        Assert.True(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, actual)));

        image.Frames[0][3, 3] = new Rgba32(4, 5, 6);
        Assert.False(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, Encode(image, new TiffEncoder()))));
    }

    [Fact]
    public void AddImageComparer_ComparesWebPSnapshotsByPixels()
    {
        using var image = CreateQuadrants(8, 8);
        var expected = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossless, Effort = 0 });
        var actual = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossless, Effort = 9 });

        var comparer = CreateComparer(SnapshotType.Webp);
        Assert.True(comparer.Equals(new SnapshotData("webp", expected), new SnapshotData("webp", actual)));
    }

    [Theory]
    [InlineData("qoi")]
    [InlineData("tga")]
    [InlineData("pnm")]
    [InlineData("cur")]
    [InlineData("ani")]
    [InlineData("avif")]
    [InlineData("heic")]
    [InlineData("jxl")]
    [InlineData("jp2")]
    [InlineData("j2k")]
    public void AddImageComparer_ComparesSnapshotsOfEveryDecodedFormatByPixels(string extension)
    {
        using var image = CreateQuadrants(16, 16, translucentAlpha: 255);
        var expected = Encode(image, CreateLosslessEncoder(extension));

        var comparer = CreateComparer(SnapshotType.Create(extension));
        Assert.True(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, Encode(image, new PngEncoder()))));

        image.Frames[0][3, 3] = new Rgba32(4, 5, 6);
        Assert.False(comparer.Equals(new SnapshotData(extension, expected), new SnapshotData(extension, Encode(image, new PngEncoder()))));
    }

    [Fact]
    public void ImageComparer_ComparesEveryFrameOfAnAnimation()
    {
        using var animation = CreateAnimation(White, Black);
        var expected = new SnapshotData("gif", Encode(animation, new GifEncoder()));
        var comparer = CreateComparer(SnapshotType.Gif);

        Assert.True(comparer.Equals(expected, new SnapshotData("gif", Encode(animation, new GifEncoder { Interlaced = true }))));

        // The same frames as an APNG file
        Assert.True(comparer.Equals(expected, new SnapshotData("png", Encode(animation, new PngEncoder()))));

        animation.Frames[1][1, 1] = White;
        Assert.False(comparer.Equals(expected, new SnapshotData("gif", Encode(animation, new GifEncoder())), out var reason));
        Assert.Equal("Frame 2 of 2: The images have different pixels.", reason);

        using var single = new Image<Rgba32>(4, 4, White);
        Assert.False(comparer.Equals(expected, new SnapshotData("gif", Encode(single, new GifEncoder())), out reason));
        Assert.Equal("The images have a different number of frames: expected 2, actual 1.", reason);
    }

    [Fact]
    public void ImageComparer_ComparesEveryPageOfATiffFile()
    {
        var expected = new SnapshotData("tiff", CreateTwoPageTiff(Black));
        var comparer = CreateComparer(SnapshotType.Tiff);

        Assert.True(comparer.Equals(expected, new SnapshotData("tiff", CreateTwoPageTiff(Black, TiffCompression.PackBits))));
        Assert.False(comparer.Equals(expected, new SnapshotData("tiff", CreateTwoPageTiff(new Rgba32(0, 0, 1))), out var reason));
        Assert.Equal("Frame 2 of 2: The images have different pixels.", reason);
    }

    [Fact]
    public void ImageComparer_ComparesEveryRepresentationOfAnIcoFile()
    {
        var expected = new SnapshotData("ico", CreateTwoEntryIco());
        var comparer = CreateComparer(SnapshotType.Ico);

        Assert.True(comparer.Equals(expected, new SnapshotData("ico", CreateTwoEntryIco(IconPayloadFormat.Png))));
        Assert.False(comparer.Equals(expected, new SnapshotData("ico", CreateTwoEntryIco(secondColor: White)), out var reason));
        Assert.Equal("Frame 2 of 2: The images have different pixels.", reason);
    }

    [Fact]
    public void ImageComparer_ExactComparison_TransparentPixelsAreEqualWhateverColorTheyHide()
    {
        var expected = CreatePng(2, 1, [new Rgba32(10, 20, 30, 0), new Rgba32(1, 2, 3)]);
        var actual = CreatePng(2, 1, [new Rgba32(40, 50, 60, 0), new Rgba32(1, 2, 3)]);
        Assert.True(ImageComparer.Instance.Equals(expected, actual));

        // A nearly transparent pixel still shows its color
        Assert.False(ImageComparer.Instance.Equals(CreatePng(2, 1, [new Rgba32(10, 20, 30, 1), new Rgba32(1, 2, 3)]), CreatePng(2, 1, [new Rgba32(40, 50, 60, 1), new Rgba32(1, 2, 3)])));
    }

    [Fact]
    public void ImageComparer_ExactComparisonSeesEveryBitOf16BitSamples()
    {
        var expected = CreatePng(new Rgba64(0x1234, 0x5678, 0x9ABC, 0xFFFF));

        // Only the low byte of the red sample differs
        Assert.False(ImageComparer.Instance.Equals(expected, CreatePng(new Rgba64(0x1235, 0x5678, 0x9ABC, 0xFFFF))));

        // The same pixel, in another file format
        using (var tiff = new Image<Rgba64>(1, 1, new Rgba64(0x1234, 0x5678, 0x9ABC, 0xFFFF)))
        {
            Assert.True(ImageComparer.Instance.Equals(expected, new SnapshotData("tiff", Encode(tiff, new TiffEncoder()))));
        }

        // The 8-bit sample v is the 16-bit sample v × 257
        var exact16Bit = CreatePng(new Rgba64(0x1212, 0x5656, 0x9A9A, 0xFFFF));
        var eightBit = CreatePng(1, 1, [new Rgba32(0x12, 0x56, 0x9A)]);
        Assert.True(ImageComparer.Instance.Equals(exact16Bit, eightBit));
        Assert.False(ImageComparer.Instance.Equals(expected, eightBit));
    }

    [Fact]
    public void ImageComparer_SimilarityRounds16BitSamplesTo8Bits()
    {
        // A checkerboard of 0x0000 and 0x0080 samples is flat once rounded to 8 bits, but 0x0081 rounds to 1
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 1f });
        var flat = CreateGray16Checkerboard(0x0000, 0x0000);

        Assert.True(comparer.Equals(flat, CreateGray16Checkerboard(0x0000, 0x0080)));
        Assert.False(comparer.Equals(flat, CreateGray16Checkerboard(0x0000, 0x0081)));
    }

    [Fact]
    public void ImageComparer_ExplainsWhyImagesDoNotMatch()
    {
        var white = CreatePng(16, 16, White);
        var black = CreatePng(16, 16, Black);
        var exact = ImageComparer.Instance;
        var similarity = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.95f });

        Assert.True(exact.Equals(white, white, out var reason));
        Assert.Null(reason);

        Assert.False(exact.Equals(white, black, out reason));
        Assert.Equal("The images have different pixels.", reason);

        Assert.False(similarity.Equals(white, black, out reason));
        Assert.StartsWith("The SSIM score 0", reason);
        Assert.EndsWith(" is below the threshold 0.95.", reason);

        Assert.False(similarity.Equals(white, CreatePng(16, 17, White), out reason));
        Assert.Equal("The images have different sizes: expected 16x16, actual 16x17.", reason);

        Assert.False(exact.Equals(white, new SnapshotData("png", [.. white.Data.AsSpan(0, 20)]), out reason));
        Assert.StartsWith("The actual snapshot cannot be decoded as an image: ", reason);
        Assert.False(exact.Equals(new SnapshotData("png", [1, 2, 3]), white, out reason));
        Assert.StartsWith("The expected snapshot cannot be decoded as an image: ", reason);
    }

    [Fact]
    public void ImageComparer_ReturnsFalseWhenTheHeaderAnnouncesAnImageTooLargeToAllocate()
    {
        using var image = CreateQuadrants(16, 16, translucentAlpha: 255);
        var expected = Encode(image, new JpegEncoder());
        var actual = CreateJpegAnnouncingAnImageTooLargeToAllocate(expected);

        Assert.False(ImageComparer.Instance.Equals(new SnapshotData("jpg", expected), new SnapshotData("jpg", actual), out var reason));
        Assert.StartsWith("The actual snapshot cannot be decoded as an image: ", reason);
        Assert.False(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0, DHashThreshold = 64 }).Equals(new SnapshotData("jpg", expected), new SnapshotData("jpg", actual)));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_AllowsSmallDifferences()
    {
        var expected = CreatePng(1, 1, Black);
        var actual = CreatePng(1, 1, new Rgba32(1, 0, 0));
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.95f });

        Assert.True(comparer.Equals(expected, actual));
        Assert.False(ImageComparer.Instance.Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_UsesWindowedSsim()
    {
        // A single SSIM over the whole image scored this pair 0.087 although the square covers less than 1% of it
        var expected = CreatePng(1024, 1024, White);
        using var square = new Image<Rgba32>(1024, 1024, White);
        FillRectangle(square, x: 462, y: 462, width: 100, height: 100, Black);
        var actual = new SnapshotData("png", Encode(square, new PngEncoder()));

        Assert.True(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.98f }).Equals(expected, actual));
        Assert.False(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.99f }).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_DetectsAlphaDifferences()
    {
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.5f });

        Assert.False(comparer.Equals(CreatePng(16, 16, new Rgba32(255, 0, 0)), CreatePng(16, 16, new Rgba32(255, 0, 0, 0))));
        Assert.False(comparer.Equals(CreatePng(16, 16, Black), CreatePng(16, 16, new Rgba32(0, 0, 0, 0))));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_TransparentPixelsAreEqualWhateverColorTheyHide()
    {
        var expected = CreatePng(16, 16, new Rgba32(255, 0, 0, 0));
        var actual = CreatePng(16, 16, new Rgba32(0, 255, 0, 0));

        Assert.True(new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 1f }).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithSimilarityThreshold_RejectsDifferentDimensions()
    {
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0, DHashThreshold = 64 });

        Assert.False(comparer.Equals(CreatePng(1, 1, White), CreatePng(2, 2, White)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_UsesInclusiveDistance(bool useDHash)
    {
        using var expectedImage = CreatePatternImage(32, 32, inverted: false);
        using var actualImage = CreatePatternImage(32, 32, inverted: true);
        var distance = useDHash
            ? ImageSimilarity.ComputeDifferenceHashDistance(expectedImage.Frames[0], actualImage.Frames[0])
            : ImageSimilarity.ComputePerceptualHashDistance(expectedImage.Frames[0], actualImage.Frames[0]);
        Assert.True(distance > 0);

        var expected = new SnapshotData("png", Encode(expectedImage, new PngEncoder()));
        var actual = new SnapshotData("png", Encode(actualImage, new PngEncoder()));
        Assert.False(CreateHashComparer(useDHash, distance - 1).Equals(expected, actual));
        Assert.True(CreateHashComparer(useDHash, distance).Equals(expected, actual));
    }

    [Fact]
    public void ImageComparer_WithHashThresholdZero_AllowsIdenticalHashes()
    {
        using var image = CreatePatternImage(32, 32, inverted: false);
        var expected = new SnapshotData("png", Encode(image, new PngEncoder()));
        var actual = new SnapshotData("bmp", Encode(image, new BmpEncoder()));

        Assert.True(new ImageComparer(new ImageComparisonSettings { DHashThreshold = 0, PHashThreshold = 0 }).Equals(expected, actual));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithOnlyHashThresholds_RejectsDifferentDimensions(bool useDHash)
    {
        // Both images reduce to the same thumbnail, so only the dimensions tell them apart
        Assert.False(CreateHashComparer(useDHash, threshold: 64).Equals(CreatePng(64, 64, Black), CreatePng(128, 32, Black)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsSmallFeatures(bool useDHash)
    {
        // Sampling only a few source pixels per thumbnail cell missed features that fall between the samples
        using var square = new Image<Rgba32>(1024, 1024, White);
        FillRectangle(square, x: 500, y: 500, width: 31, height: 31, Black);
        using var band = new Image<Rgba32>(1024, 1024, White);
        FillRectangle(band, x: 700, y: 0, width: 11, height: 1024, Black);

        var comparer = CreateHashComparer(useDHash, threshold: 0);
        var expected = CreatePng(1024, 1024, White);
        Assert.False(comparer.Equals(expected, new SnapshotData("png", Encode(square, new PngEncoder()))));
        Assert.False(comparer.Equals(expected, new SnapshotData("png", Encode(band, new PngEncoder()))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsUniformColorDifferences(bool useDHash)
    {
        // Uniform images have no structure, so their hashes are equal whatever their color
        var white = CreatePng(16, 16, White);
        var black = CreatePng(16, 16, Black);
        var gray = CreatePng(16, 16, new Rgba32(0x80, 0x80, 0x80));

        Assert.False(CreateHashComparer(useDHash, threshold: 63).Equals(white, black));
        Assert.False(CreateHashComparer(useDHash, threshold: 5).Equals(white, gray));
        Assert.True(CreateHashComparer(useDHash, threshold: 64).Equals(white, black));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThresholdZero_IgnoresImperceptibleDifferencesInFlatImages(bool useDHash)
    {
        // The DCT of a flat image is rounding noise, which used to decide most pHash bits
        Assert.True(CreateHashComparer(useDHash, threshold: 0).Equals(CreatePng(100, 60, new Rgba32(0x80, 0x80, 0x80)), CreatePng(100, 60, new Rgba32(0x81, 0x80, 0x80))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_DetectsAlphaDifferences(bool useDHash)
    {
        var comparer = CreateHashComparer(useDHash, threshold: 0);

        Assert.False(comparer.Equals(CreatePng(16, 16, new Rgba32(255, 0, 0)), CreatePng(16, 16, new Rgba32(255, 0, 0, 0))));
        Assert.False(comparer.Equals(CreatePng(16, 16, Black), CreatePng(16, 16, new Rgba32(0, 0, 0, 0))));
        Assert.False(comparer.Equals(CreatePng(16, 16, White), CreatePng(16, 16, new Rgba32(255, 255, 255, 0))));

        // Only the alpha channel draws the square
        using var square = new Image<Rgba32>(64, 64, Black);
        FillRectangle(square, x: 16, y: 16, width: 24, height: 24, new Rgba32(0, 0, 0, 0));
        Assert.False(comparer.Equals(CreatePng(64, 64, Black), new SnapshotData("png", Encode(square, new PngEncoder()))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ImageComparer_WithHashThreshold_TransparentPixelsAreEqualWhateverColorTheyHide(bool useDHash)
    {
        Assert.True(CreateHashComparer(useDHash, threshold: 0).Equals(CreatePng(16, 16, new Rgba32(255, 0, 0, 0)), CreatePng(16, 16, new Rgba32(0, 255, 0, 0))));
    }

    [Fact]
    public void ImageComparer_WithMultipleThresholds_RequiresAllToPass()
    {
        using var expected = CreatePatternImage(32, 32, inverted: false);
        using var actual = CreatePatternImage(32, 32, inverted: true);
        var comparer = new ImageComparer(new ImageComparisonSettings { DHashThreshold = 64, PHashThreshold = 0 });

        Assert.False(comparer.Equals(new SnapshotData("png", Encode(expected, new PngEncoder())), new SnapshotData("png", Encode(actual, new PngEncoder()))));
    }

    [Fact]
    public void ImageComparer_ExplainsWhichHashCheckFailed()
    {
        using var expectedImage = CreatePatternImage(32, 32, inverted: false);
        using var actualImage = CreatePatternImage(32, 32, inverted: true);
        var dHashDistance = ImageSimilarity.ComputeDifferenceHashDistance(expectedImage.Frames[0], actualImage.Frames[0]);
        var pHashDistance = ImageSimilarity.ComputePerceptualHashDistance(expectedImage.Frames[0], actualImage.Frames[0]);
        var expected = new SnapshotData("png", Encode(expectedImage, new PngEncoder()));
        var actual = new SnapshotData("png", Encode(actualImage, new PngEncoder()));

        Assert.False(new ImageComparer(new ImageComparisonSettings { DHashThreshold = 0, PHashThreshold = 0 }).Equals(expected, actual, out var reason));
        Assert.Equal($"The dHash distance {dHashDistance} is above the threshold 0. The pHash distance {pHashDistance} is above the threshold 0.", reason);

        Assert.False(new ImageComparer(new ImageComparisonSettings { DHashThreshold = 64, PHashThreshold = 0 }).Equals(expected, actual, out reason));
        Assert.Equal($"The pHash distance {pHashDistance} is above the threshold 0.", reason);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ImageComparisonSettings_RejectsInvalidSimilarityThresholds(float threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { SimilarityThreshold = threshold });
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(null)]
    public void ImageComparisonSettings_AcceptsValidSimilarityThresholds(float? threshold)
    {
        Assert.Equal(threshold, new ImageComparisonSettings { SimilarityThreshold = threshold }.SimilarityThreshold);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void ImageComparisonSettings_RejectsInvalidHashThresholds(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { DHashThreshold = threshold });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageComparisonSettings { PHashThreshold = threshold });
    }

    [Fact]
    public void Validate_ReportsWhyImagesDoNotMatch()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = CreateSettings(directory, "png", SnapshotUpdateStrategy.Disallow);
        settings.Comparers.AddImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.99f });
        var verifiedPath = directory.GetFullPath("snapshot_0.verified.png");
        File.WriteAllBytes(verifiedPath, CreatePng(16, 16, White).Data);

        var exception = Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(CreatePng(16, 16, Black).Data, SnapshotType.Png, settings));

        Assert.Matches(@"Changed snapshot files:\r?\n  \* " + Regex.Escape(verifiedPath.Value) + @"\r?\n    The SSIM score -?[0-9.]+ is below the threshold 0\.99\.", exception.Message);
    }

    [Fact]
    public void Validate_MatchesABmpSnapshotWhoseMetadataDiffers_WhenTheImageComparerIsEnabled()
    {
        using var directory = TemporaryDirectory.Create();
        using var image = new Image<Rgb24>(1, 1, new Rgb24(1, 2, 3));
        image.Metadata.Resolution = new ImageResolution(72, 72);
        var verifiedData = Encode(image, new BmpEncoder());
        var verifiedPath = directory.GetFullPath("snapshot_0.verified.bmp");
        File.WriteAllBytes(verifiedPath, verifiedData);

        var settings = CreateSettings(directory, "bmp", SnapshotUpdateStrategy.Disallow);
        settings.Comparers.AddImageComparer();
        image.Metadata.Resolution = new ImageResolution(96, 96);
        Snapshot.Validate(Encode(image, new BmpEncoder()), SnapshotType.Bmp, settings);

        Assert.Equal(verifiedData, File.ReadAllBytes(verifiedPath));
    }

    private static SnapshotType[] ImageTypes =>
    [
        SnapshotType.Bmp, SnapshotType.Png, SnapshotType.Jpeg, SnapshotType.Tiff, SnapshotType.Webp, SnapshotType.Gif, SnapshotType.Ico,
        SnapshotType.Avif, SnapshotType.Heic, SnapshotType.JpegXl, SnapshotType.Jpeg2000,
        "apng", "dib", "cur", "ani", "qoi", "tga", "icb", "vda", "vst", "pnm", "pbm", "pgm", "ppm", "pam",
        "avifs", "heif", "heics", "heifs", "j2k", "j2c", "jpc", "jpx", "jpf", "jph", "jhc",
    ];

    private static SnapshotSettings CreateSettings(TemporaryDirectory directory, string extension, SnapshotUpdateStrategy strategy)
    {
        return new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = strategy,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + extension),
        };
    }

    private static ISnapshotComparer CreateComparer(SnapshotType type)
    {
        var settings = new SnapshotSettings();
        settings.Comparers.AddImageComparer();
        return settings.Comparers.Get(type);
    }

    private static ImageEncoder CreateLosslessEncoder(string extension) => extension switch
    {
        "qoi" => new QoiEncoder(),
        "tga" => new TgaEncoder(),
        "pnm" => new PnmEncoder(),
        "cur" => new IcoEncoder { Kind = IconKind.Cursor },
        "ani" => new AniEncoder(),
        "avif" => new AvifEncoder { Compression = AvifCompression.Lossless },
        "heic" => new HeicEncoder { Compression = HeicCompression.Lossless },
        "jxl" => new JpegXlEncoder { Compression = JpegXlCompression.Lossless },
        "jp2" => new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossless },
        "j2k" => new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossless, Container = Jpeg2000Container.Codestream },
        _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, message: null),
    };

    private static ImageComparer CreateHashComparer(bool useDHash, int threshold)
    {
        return new ImageComparer(useDHash ? new ImageComparisonSettings { DHashThreshold = threshold } : new ImageComparisonSettings { PHashThreshold = threshold });
    }

    private static byte[] ReadFixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", name));

    private static byte[] Encode(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static SnapshotData CreatePng(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        return new SnapshotData("png", Encode(image, new PngEncoder()));
    }

    private static SnapshotData CreatePng(int width, int height, Rgba32[] pixels)
    {
        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        return new SnapshotData("png", Encode(image, new PngEncoder()));
    }

    private static SnapshotData CreatePng(Rgba64 pixel)
    {
        using var image = new Image<Rgba64>(1, 1, pixel);
        return new SnapshotData("png", Encode(image, new PngEncoder()));
    }

    private static SnapshotData CreateGray16Checkerboard(ushort first, ushort second)
    {
        const int Size = 16;
        var pixels = new Gray16[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                pixels[y * Size + x] = new Gray16((x + y) % 2 is 0 ? first : second);
            }
        }

        using var image = Image.ImportPixelData<Gray16>(pixels, Size, Size);
        return new SnapshotData("png", Encode(image, new PngEncoder()));
    }

    /// <summary>Four quadrants of different colors, the last one with an alpha of <paramref name="translucentAlpha"/>.</summary>
    private static Image<Rgba32> CreateQuadrants(int width, int height, byte translucentAlpha = 128)
    {
        var image = new Image<Rgba32>(width, height);
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = (x < width / 2, y < height / 2) switch
                    {
                        (true, true) => new Rgba32(255, 0, 0),
                        (false, true) => new Rgba32(0, 255, 0),
                        (true, false) => new Rgba32(0, 0, 255),
                        _ => new Rgba32(255, 255, 0, translucentAlpha),
                    };
                }
            }
        });

        return image;
    }

    private static Image<Rgba32> CreatePatternImage(int width, int height, bool inverted)
    {
        var pixels = new Rgba32[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (byte)((x * 17 + y * 31 + x * y * 3) % 256);
                if (inverted)
                {
                    value = (byte)(255 - value);
                }

                pixels[y * width + x] = new Rgba32(value, value, value);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    /// <summary>A 4x4 animation of two frames: <paramref name="first"/>, then <paramref name="second"/>.</summary>
    private static Image<Rgba32> CreateAnimation(Rgba32 first, Rgba32 second)
    {
        var animation = new Image<Rgba32>(4, 4, first);
        animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);
        var frame = animation.AppendFrame();
        frame.Metadata.Duration = FrameDuration.FromMilliseconds(100);
        frame.ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                pixels.GetRowSpan(y).Fill(second);
            }
        });

        return animation;
    }

    private static void FillRectangle(Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var row = y; row < y + height; row++)
            {
                pixels.GetRowSpan(row).Slice(x, width).Fill(color);
            }
        });
    }

    private static Rgba32[] GetPixels(byte[] data)
    {
        using var image = Image.Load<Rgba32>(data);
        return GetPixels(image);
    }

    private static Rgba32[] GetPixels(Image<Rgba32> image) => GetPixels(image.Frames[0]);

    private static Rgba32[] GetPixels(ImageFrame<Rgba32> frame)
    {
        var pixels = new Rgba32[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }

    /// <summary>A 1x1 GIF file of two white frames, written by hand.</summary>
    private static byte[] CreateTwoFrameGif()
    {
        return
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61,
            0x01, 0x00, 0x01, 0x00,
            0x80, 0x01, 0x00,
            0xFF, 0xFF, 0xFF,
            0x00, 0x00, 0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x2C,
            0x00, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x01, 0x00,
            0x00,
            0x02,
            0x02, 0x44, 0x01,
            0x00,
            0x3B,
        ];
    }

    /// <summary>An ICO file of a 16x16 white icon and a 32x32 icon of <paramref name="secondColor"/> (black by default).</summary>
    private static byte[] CreateTwoEntryIco(IconPayloadFormat payloadFormat = IconPayloadFormat.Auto, Rgba32? secondColor = null)
    {
        using var collection = ImageCollection.Create(ImageCollectionKind.Representations);
        using var small = new Image<Rgba32>(16, 16, White);
        using var large = new Image<Rgba32>(32, 32, secondColor ?? Black);
        collection.Add(small);
        collection.Add(large);
        using var stream = new MemoryStream();
        collection.Save(stream, new IcoEncoder { PayloadFormat = payloadFormat });
        return stream.ToArray();
    }

    /// <summary>A TIFF file of a white page and a page of <paramref name="secondColor"/>.</summary>
    private static byte[] CreateTwoPageTiff(Rgba32 secondColor, TiffCompression compression = TiffCompression.None)
    {
        using var collection = ImageCollection.Create(ImageCollectionKind.Pages);
        using var first = new Image<Rgba32>(4, 4, White);
        using var second = new Image<Rgba32>(4, 4, secondColor);
        collection.Add(first);
        collection.Add(second);
        using var stream = new MemoryStream();
        collection.Save(stream, new TiffEncoder { Compression = compression });
        return stream.ToArray();
    }

    /// <summary>Inserts a comment segment right after the start of image marker: the pixels do not change.</summary>
    private static byte[] AddJpegComment(byte[] jpeg, string comment)
    {
        var text = Encoding.ASCII.GetBytes(comment);
        var segment = new byte[4 + text.Length];
        segment[0] = 0xFF;
        segment[1] = 0xFE;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(text.Length + 2));
        text.CopyTo(segment, 4);
        return [.. jpeg.AsSpan(0, 2), .. segment, .. jpeg.AsSpan(2)];
    }

    private static byte[] CreateJpegAnnouncingAnImageTooLargeToAllocate(byte[] jpeg)
    {
        // 37182 × 57756 pixels is a valid int, but beyond the default resource limits
        var result = (byte[])jpeg.Clone();
        var startOfFrame = result.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0]);
        Assert.True(startOfFrame >= 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(startOfFrame + 5), 57756);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(startOfFrame + 7), 37182);
        return result;
    }
}
