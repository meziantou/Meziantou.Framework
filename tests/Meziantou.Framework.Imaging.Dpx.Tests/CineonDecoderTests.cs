using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dpx;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Kodak Cineon decoding of files assembled by <see cref="CineonFileBuilder"/> (Image File Format Proposal for Digital
/// Pictures, draft 4.5): the cells of every packing in both byte orders, the channel designators, the header fields, the
/// orientation and the errors. The expected samples are the code values scaled by <c>(v × max' + max / 2) / max</c>.
/// </summary>
public sealed class CineonDecoderTests
{
    [Theory]
    [InlineData((byte)5, true)]
    [InlineData((byte)5, false)]
    [InlineData((byte)6, true)]
    [InlineData((byte)0x85, false)]
    public void TenBitRgbSamplesAreScaledToSixteenBits(byte packing, bool bigEndian)
    {
        uint[] samples = [0, 1023, 1, 1022, 511, 3, 95, 685, 512, 7, 8, 9];
        var data = new CineonFileBuilder { Width = 2, Height = 2, BitsPerSample = 10, Packing = packing, BigEndian = bigEndian, Samples = samples }.Build();
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb48, image.PixelFormat);
        using var pixels = image.CloneAs<Rgba64>();
        for (var i = 0; i < 4; i++)
        {
            var pixel = pixels.Frames[0][i % 2, i / 2];
            Assert.Equal(new Rgba64(Scale(samples[i * 3], 10), Scale(samples[(i * 3) + 1], 10), Scale(samples[(i * 3) + 2], 10), 65535), pixel);
        }

        Assert.Equal(ImageFormat.Cineon, Image.DetectFormat(data));
        Assert.Equal(bigEndian, image.Metadata.CineonHeader!.IsBigEndian);
        Assert.Equal(packing, image.Metadata.CineonHeader.Packing);
    }

    [Fact]
    public void TheCommonLayoutIsOnePixelPerLeftJustifiedWord()
    {
        // R, G, B from the most significant bit of a big-endian 32-bit word, two padding bits at the bottom
        var data = new CineonFileBuilder { Width = 1, Height = 1, BitsPerSample = 10, RawData = new byte[] { 0x00, 0x40, 0x10, 0x0C } }.Build();
        using var image = Image.Load<Rgba64>(data);
        Assert.Equal(new Rgba64(Scale(1, 10), Scale(1, 10), Scale(3, 10), 65535), image.Frames[0][0, 0]);
    }

    [Fact]
    public void GrayImagesShareTheirCells()
    {
        // One channel: three 10-bit samples per left-justified 32-bit cell, the line ends on a cell
        uint[] samples = [100, 200, 300, 400, 500];
        var data = new CineonFileBuilder { Width = 5, Height = 1, BitsPerSample = 10, Designators = [0], Samples = samples }.Build();
        Assert.HasCount(2048 + 8, data);
        using var image = Image.Load(data);
        Assert.Equal(PixelFormat.Gray16, image.PixelFormat);
        var frame = image.CloneAs<Gray16>().Frames[0];
        Assert.Equal(samples.Select(sample => Scale(sample, 10)).ToArray(), Enumerable.Range(0, 5).Select(x => frame[x, 0].Value).ToArray());
    }

    [Fact]
    public void EightBitSamplesAreAStreamWhoseLinesEndOnACell()
    {
        uint[] samples = [.. Enumerable.Range(0, 30).Select(i => (uint)(i * 8))];
        var data = new CineonFileBuilder { Width = 5, Height = 2, BitsPerSample = 8, Samples = samples }.Build();
        Assert.HasCount(2048 + 32, data);
        using var image = Image.Load<Rgb24>(data);
        Assert.Equal(new Rgb24(120, 128, 136), image.Frames[0][0, 1]);
    }

    [Fact]
    public void ChannelsArePlacedByTheirDesignators()
    {
        var data = new CineonFileBuilder { Width = 1, Height = 1, BitsPerSample = 8, Designators = [3, 2, 1], Samples = [10, 20, 30] }.Build();
        using var image = Image.Load<Rgb24>(data);
        Assert.Equal(new Rgb24(30, 20, 10), image.Frames[0][0, 0]);
        Assert.Equal([3, 2, 1], image.Metadata.CineonHeader!.ChannelDesignators.Select(value => (int)value).ToArray());

        // Designators that are not red, green and blue keep the order of the file
        var other = new CineonFileBuilder { Width = 1, Height = 1, BitsPerSample = 8, Designators = [0, 0, 0], Samples = [10, 20, 30] }.Build();
        using var otherImage = Image.Load<Rgb24>(other);
        Assert.Equal(new Rgb24(10, 20, 30), otherImage.Frames[0][0, 0]);
    }

    [Theory]
    [InlineData(0, ExifOrientation.TopLeft)]
    [InlineData(1, ExifOrientation.BottomLeft)]
    [InlineData(2, ExifOrientation.TopRight)]
    [InlineData(3, ExifOrientation.BottomRight)]
    [InlineData(4, ExifOrientation.LeftTop)]
    [InlineData(5, ExifOrientation.RightTop)]
    [InlineData(6, ExifOrientation.LeftBottom)]
    [InlineData(7, ExifOrientation.RightBottom)]
    public void OrientationIsReportedNotApplied(byte code, ExifOrientation expected)
    {
        var data = new CineonFileBuilder { Width = 2, Height = 1, BitsPerSample = 8, Designators = [0], Orientation = code, Samples = [10, 20] }.Build();
        using var image = Image.Load<Gray8>(data);
        Assert.Equal(expected, image.Metadata.Orientation);
        Assert.Equal(10, image.Frames[0][0, 0].Value);
    }

    [Fact]
    public void HeaderFieldsAreReported()
    {
        var builder = new CineonFileBuilder { Width = 1, Height = 1, BitsPerSample = 10, Samples = [1, 2, 3], Sense = 1 };
        builder.Text[(32, 100)] = "frame.cin";
        builder.Text[(1036, 32)] = "ACADEMY";
        builder.ByteFields[1024] = 12;
        builder.ByteFields[1025] = 34;
        builder.ByteFields[1026] = 5;
        builder.Words[1028] = 678901;
        builder.Words[1032] = 2345;
        builder.Words[1068] = 1001;
        var header = Image.Identify(builder.Build()).Metadata.CineonHeader!;
        Assert.Equal("V4.5", header.Version);
        Assert.Equal("frame.cin", header.FileName);
        Assert.Equal("ACADEMY", header.FilmFormat);
        Assert.Equal("1234056789012345", header.FilmEdgeCode);
        Assert.Equal(1001u, header.FramePosition);
        Assert.True(header.IsNegative);
        Assert.Equal(10, header.BitsPerSample);
        Assert.Equal(2.048f, header.MaximumQuantities[0]);
        Assert.Equal([0.3127f, 0.329f, 0.64f, 0.33f, 0.3f, 0.6f, 0.15f, 0.06f], header.Chromaticities!.ToArray());
        Assert.Null(header.Gamma);
    }

    public static TheoryData<string, Type, string?> Rejections() => new()
    {
        { "two-channels", typeof(UnsupportedImageFeatureException), "Images of 2 channels" },
        { "line-interleave", typeof(UnsupportedImageFeatureException), "Data interleave 1" },
        { "signed", typeof(UnsupportedImageFeatureException), "Signed samples" },
        { "sizes", typeof(UnsupportedImageFeatureException), "Channels of different sizes or depths" },
        { "depths", typeof(UnsupportedImageFeatureException), "Channels of different sizes or depths" },
        { "bit-fields", typeof(UnsupportedImageFeatureException), "Packing 0" },
        { "no-channel", typeof(InvalidImageContentException), null },
        { "orientation-8", typeof(InvalidImageContentException), null },
        { "small-cells", typeof(InvalidImageContentException), null },
        { "data-offset", typeof(InvalidImageContentException), null },
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void InvalidAndUnsupportedFilesAreRejectedByTheHeader(string name, Type exception, string? feature)
    {
        var builder = new CineonFileBuilder { Width = 2, Height = 1, BitsPerSample = 10, Samples = [1, 2, 3, 4, 5, 6] };
        Action<CineonFileBuilder> edit = name switch
        {
            "two-channels" => b => (b.Designators, b.Samples) = ([0, 0], [1, 2, 3, 4]),
            "line-interleave" => b => b.Interleave = 1,
            "signed" => b => b.Sign = 1,
            "sizes" => b => b.OtherChannelWidth = 3,
            "depths" => b => b.OtherChannelBits = 12,
            "bit-fields" => b => (b.Packing, b.RawData) = (0, new byte[8]),
            "no-channel" => b => b.ChannelCount = 0,
            "orientation-8" => b => b.Orientation = 8,
            "small-cells" => b => (b.Packing, b.RawData) = (1, new byte[8]),
            _ => b => b.Words[4] = 1000,
        };
        edit(builder);
        var data = builder.Build();
        var error = Record(() => Image.Load(data).Dispose());
        Assert.Equal(exception, error?.GetType());
        Assert.Equal(exception, Record(() => Image.Identify(data))?.GetType());
        if (feature is not null)
        {
            Assert.Equal(feature, ((UnsupportedImageFeatureException)error!).Feature);
        }

        Assert.Equal(ImageFormat.Cineon, error switch { InvalidImageContentException invalid => invalid.Format, UnsupportedImageFeatureException unsupported => unsupported.Format, _ => ImageFormat.Unknown });
    }

    [Fact]
    public void CineonFilesAreSavedByTheirExtension()
    {
        Assert.IsType<Formats.CineonEncoder>(Formats.ImageEncoder.Create(ImageFormat.Cineon));
        Assert.IsType<Formats.CineonEncoder>(Formats.ImageEncoder.FromFileExtension(".cin"));
        using var image = new Image<Rgb24>(1, 1, new Rgb24(1, 2, 3));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cin");
        try
        {
            image.Save(path);
            using var decoded = Image.Load<Rgb24>(path);
            Assert.Equal(ImageFormat.Cineon, decoded.Metadata.SourceFormat);
            Assert.Equal(new Rgb24(1, 2, 3), decoded.Frames[0][0, 0]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ushort Scale(uint value, int bits)
    {
        var max = (1UL << bits) - 1;
        return (ushort)(((value * 65535UL) + (max / 2)) / max);
    }

    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
