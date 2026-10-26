using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Boundary and one-over checks of the limits that apply outside decoding: canvases of
/// constructors and raw imports, resize targets, quarter-turn rotations, and the live-allocation budget of images and
/// writers. Decoders and identification are covered for every corpus input by the conformance
/// <c>ResourceLimitBoundaryTests</c>. Writers enforce only <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>: the
/// canvas, frame, pixel, encoded-byte and metadata limits bound what is read from one input.
/// </summary>
public sealed class LimitBoundaryTests
{
    public static TheoryData<string> Creators => ["constructor", "constructor-fill", "import-data", "import-bytes"];

    [Theory]
    [MemberData(nameof(Creators))]
    public void CanvasLimitsOfImageCreationAreInclusive(string creator)
    {
        // Width, height and frame pixels: the limit itself is accepted, one more fails with Kind, Limit and Requested
        Create(creator, 7, 3, Configuration(new ImageResourceLimits { MaxWidth = 7, MaxHeight = 3, MaxFramePixels = 21 })).Dispose();
        AssertLimit(ImageResourceLimitKind.Width, 7, 8, () => Create(creator, 8, 3, Configuration(new ImageResourceLimits { MaxWidth = 7 })));
        AssertLimit(ImageResourceLimitKind.Height, 3, 4, () => Create(creator, 7, 4, Configuration(new ImageResourceLimits { MaxHeight = 3 })));
        AssertLimit(ImageResourceLimitKind.FramePixels, 21, 24, () => Create(creator, 8, 3, Configuration(new ImageResourceLimits { MaxFramePixels = 21 })));
        AssertLimit(ImageResourceLimitKind.FramePixels, 20, 21, () => Create(creator, 7, 3, Configuration(new ImageResourceLimits { MaxFramePixels = 20 })));
    }

    [Theory]
    [MemberData(nameof(Creators))]
    public void LiveAllocationOfImageCreationIsChargedAtTheRentedCapacity(string creator)
    {
        // 10x10 Rgba32 = 400 bytes in one slab, rented from the 448-byte size class (256 + 3 * 64)
        const long Capacity = 448;
        Create(creator, 10, 10, Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = Capacity })).Dispose();
        AssertLimit(ImageResourceLimitKind.LiveAllocationBytes, Capacity - 1, Capacity, () => Create(creator, 10, 10, Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = Capacity - 1 })));
    }

    [Fact]
    public void FloatingPointPixelsAreChargedAtSixteenBytesEach()
    {
        // 10x10 RgbaFloat = 1,600 bytes in one slab, charged at the capacity of its size class
        long capacity;
        using (var probe = new Image<RgbaFloat>(10, 10))
        {
            capacity = probe.Owner.Scope.LiveBytes;
        }

        Assert.InRange(capacity, 1600, 2047);
        new Image<RgbaFloat>(10, 10, Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = capacity })).Dispose();
        AssertLimit(ImageResourceLimitKind.LiveAllocationBytes, capacity - 1, capacity, () => new Image<RgbaFloat>(10, 10, Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = capacity - 1 })).Dispose());

        // A conversion to floating point is charged before anything is converted: twice the bytes of Rgba64 pixels
        using var source = new Image<Rgba64>(10, 10, Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = capacity - 1 }));
        AssertLimit(ImageResourceLimitKind.LiveAllocationBytes, capacity - 1, capacity, () => source.CloneAs<RgbaFloat>().Dispose());
        using var halves = source.CloneAs<RgbaHalf>();
        Assert.Equal(source.Owner.Scope.LiveBytes, halves.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void ResizeTargetsAreCheckedInclusively()
    {
        var limits = new ImageResourceLimits { MaxWidth = 12, MaxHeight = 9, MaxFramePixels = 96 };
        using var image = new Image<Rgba32>(4, 3, new Rgba32(1, 2, 3, 255), Configuration(limits));
        image.Resize(new ResizeOptions(12, 8) { Mode = ResizeMode.Stretch }, XunitCancellationToken); // 96 pixels, width at the limit
        image.Resize(new ResizeOptions(10, 9) { Mode = ResizeMode.Stretch }, XunitCancellationToken); // height at the limit
        Assert.Equal(new Size(10, 9), image.Size);
        AssertLimit(ImageResourceLimitKind.Width, 12, 13, () => image.Resize(new ResizeOptions(13, 1) { Mode = ResizeMode.Stretch }, XunitCancellationToken));
        AssertLimit(ImageResourceLimitKind.Height, 9, 10, () => image.Resize(new ResizeOptions(1, 10) { Mode = ResizeMode.Stretch }, XunitCancellationToken));
        AssertLimit(ImageResourceLimitKind.FramePixels, 96, 99, () => image.Resize(new ResizeOptions(11, 9) { Mode = ResizeMode.Stretch }, XunitCancellationToken));
        Assert.Equal(new Size(10, 9), image.Size); // failures leave the image unchanged
    }

    [Fact]
    public void AutoCropExpandedCanvasesAreCheckedInclusively()
    {
        // 3x3 content at (2, 2) of a 7x7 canvas: a padding of 3 gives 9x9, a padding of 4 gives 11x11
        Image<Gray8> Create(ImageResourceLimits limits)
        {
            var image = new Image<Gray8>(7, 7, new Gray8(255), Configuration(limits));
            for (var y = 2; y < 5; y++)
            {
                for (var x = 2; x < 5; x++)
                {
                    image.Frames[0][x, y] = new Gray8(0);
                }
            }

            return image;
        }

        using (var image = Create(new ImageResourceLimits { MaxWidth = 9, MaxHeight = 9, MaxFramePixels = 81 }))
        {
            Assert.True(image.AutoCrop(new AutoCropOptions { PaddingX = 3, PaddingY = 3 }, XunitCancellationToken)); // every limit reached
            Assert.Equal(new Size(9, 9), image.Size);
        }

        using var limited = Create(new ImageResourceLimits { MaxWidth = 10, MaxHeight = 9, MaxFramePixels = 80 });
        AssertLimit(ImageResourceLimitKind.Width, 10, 11, () => limited.AutoCrop(new AutoCropOptions { PaddingX = 4, PaddingY = 3 }, XunitCancellationToken));
        AssertLimit(ImageResourceLimitKind.Height, 9, 11, () => limited.AutoCrop(new AutoCropOptions { PaddingX = 3, PaddingY = 4 }, XunitCancellationToken));
        AssertLimit(ImageResourceLimitKind.FramePixels, 80, 81, () => limited.AutoCrop(new AutoCropOptions { PaddingX = 3, PaddingY = 3 }, XunitCancellationToken));
        Assert.Equal(new Size(7, 7), limited.Size); // failures leave the image unchanged

        // Clamping never enlarges the canvas
        Assert.False(limited.AutoCrop(new AutoCropOptions { PaddingX = 4, PaddingY = 4, PaddingMode = AutoCropPaddingMode.Contain }, XunitCancellationToken));
    }

    [Fact]
    public void RotatedCanvasesAreCheckedInclusively()
    {
        using (var wide = new Image<Gray8>(5, 3, Configuration(new ImageResourceLimits { MaxWidth = 5, MaxHeight = 5 })))
        {
            wide.Rotate(RotateMode.Rotate90, XunitCancellationToken); // 3x5: height at the limit
            Assert.Equal(new Size(3, 5), wide.Size);
        }

        using (var wide = new Image<Gray8>(5, 3, Configuration(new ImageResourceLimits { MaxWidth = 5, MaxHeight = 4 })))
        {
            AssertLimit(ImageResourceLimitKind.Height, 4, 5, () => wide.Rotate(RotateMode.Rotate270, XunitCancellationToken));
            wide.Rotate(RotateMode.Rotate180, XunitCancellationToken); // no axis swap
            Assert.Equal(new Size(5, 3), wide.Size);
        }

        using var tall = new Image<Gray8>(3, 5, Configuration(new ImageResourceLimits { MaxWidth = 4, MaxHeight = 5 }));
        AssertLimit(ImageResourceLimitKind.Width, 4, 5, () => tall.Rotate(RotateMode.Rotate90, XunitCancellationToken));
        Assert.Equal(new Size(3, 5), tall.Size);
    }

    public static TheoryData<string> Encoders => ["png", "apng", "gif", "jpeg", "jpeg-progressive", "webp", "webp-lossy", "webp-animated", "qoi", "avif", "avif-lossy", "avif-animated", "heic", "heic-lossy", "heic-animated", "jxl", "jxl-fast", "jxl-animated", "jxl-lossy", "jxl-lossy-progressive", "jxl-progressive", "jp2", "jp2-lossy", "j2k"];

    [Theory]
    [MemberData(nameof(Encoders))]
    public void WriterStateIsBudgetedAtTheBoundary(string encoder)
    {
        using var image = new Image<Rgba32>(37, 23);
        image.Frames[0].ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x * 7), (byte)(y * 11), (byte)(x ^ y), 255);
                }
            }
        });

        // A frame of a JPEG XL animation must have a duration
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);

        // Bisect the smallest budget the writer accepts (its private state at actual rented capacity), then check one byte less
        var low = 1L;
        var high = ImageResourceLimits.DefaultMaxLiveAllocationBytes;
        Assert.Null(TryWrite(image, encoder, high));
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (TryWrite(image, encoder, middle) is null)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        Assert.Null(TryWrite(image, encoder, high));
        var exception = TryWrite(image, encoder, high - 1);
        Assert.NotNull(exception);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Equal(high - 1, exception.Limit);
        Assert.True(exception.Requested > high - 1);
    }

    private static ImageResourceLimitException? TryWrite(Image<Rgba32> image, string encoder, long budget)
    {
        var options = new ImageWriterOptions(image.Size)
        {
            Encoder = encoder switch
            {
                "png" => new PngEncoder(),
                "apng" => new PngEncoder { AnimationMode = PngAnimationMode.Animated },
                "gif" => new GifEncoder(),
                "webp" or "webp-animated" => new WebPEncoder(),
                "webp-lossy" => new WebPEncoder { Compression = WebPCompression.Lossy },
                "qoi" => new QoiEncoder(),
                "avif" or "avif-animated" => new AvifEncoder(),
                "avif-lossy" => new AvifEncoder { Compression = AvifCompression.Lossy },
                "heic" or "heic-animated" => new HeicEncoder(),
                "heic-lossy" => new HeicEncoder { Compression = HeicCompression.Lossy },
                "jxl" or "jxl-animated" => new JpegXlEncoder(),
                "jxl-fast" => new JpegXlEncoder { Effort = 0 },
                "jpeg-progressive" => new JpegEncoder { Process = JpegEncodingProcess.Progressive },
                "jxl-lossy" => new JpegXlEncoder { Compression = JpegXlCompression.Lossy },
                "jxl-lossy-progressive" => new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true },
                "jxl-progressive" => new JpegXlEncoder { Progressive = true },
                "jp2" => new Jpeg2000Encoder(),
                "jp2-lossy" => new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy },
                "j2k" => new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream },
                _ => new JpegEncoder(),
            },
            ExpectedFrameCount = encoder is "apng" or "webp-animated" or "avif-animated" or "heic-animated" or "jxl-animated" ? 2 : 1,
            Configuration = Configuration(new ImageResourceLimits { MaxLiveAllocationBytes = budget }),
        };
        using var output = new MemoryStream();
        try
        {
            using var writer = Image.CreateWriter<Rgba32>(output, options);
            for (var i = 0; i < options.ExpectedFrameCount; i++)
            {
                writer.WriteFrame(image.Frames[0]);
            }

            writer.Complete();
            return null;
        }
        catch (ImageResourceLimitException exception)
        {
            return exception;
        }
    }

    private static Image<Rgba32> Create(string creator, int width, int height, ImageConfiguration configuration) => creator switch
    {
        "constructor" => new Image<Rgba32>(width, height, configuration),
        "constructor-fill" => new Image<Rgba32>(width, height, new Rgba32(1, 2, 3, 4), configuration),
        "import-data" => Image.ImportPixelData<Rgba32>(new Rgba32[width * height], width, height, configuration: configuration),
        _ => Image.ImportPixelBytes<Rgba32>(new byte[width * height * 4], width, height, configuration: configuration),
    };

    private static ImageConfiguration Configuration(ImageResourceLimits limits) => new() { Limits = limits };

    private static void AssertLimit(ImageResourceLimitKind kind, long limit, long requested, Action action)
    {
        var exception = Assert.Throws<ImageResourceLimitException>(action);
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(limit, exception.Limit);
        Assert.Equal(requested, exception.Requested);
    }

    private static void AssertLimit(ImageResourceLimitKind kind, long limit, long requested, Func<Image<Rgba32>> action)
        => AssertLimit(kind, limit, requested, () => action().Dispose());
}
