using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Avif;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// AVIF encoding through the public save API. The structure of the output is validated with the independent harness reader
/// (<see cref="ReferenceAvif"/>: boxes, items, properties). The harness has no AV1 decoder: the samples are read back with
/// the decoder of the library, which the conformance vectors pin to the reference decoders byte for byte, and the reference
/// decoders themselves decode the output of the encoder in the interoperability tests.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class AvifEncoderTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaultsAndRanges()
    {
        var encoder = new AvifEncoder();
        Assert.Equal(ImageFormat.Avif, encoder.Format);
        Assert.Equal(AvifCompression.Lossless, encoder.Compression);
        Assert.Equal(75, encoder.Quality);
        Assert.Equal(5, encoder.Effort);
        Assert.Equal(AvifChromaSubsampling.Auto, encoder.ChromaSubsampling);
        Assert.Null(encoder.BitDepth);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.False(encoder.ClearTransparentColors);
        Assert.Equal(FrameDurationRounding.RequireExact, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);

        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Compression = (AvifCompression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { ChromaSubsampling = (AvifChromaSubsampling)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { BitDepth = 9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { BitDepth = 16 });
        foreach (var bitDepth in new int?[] { null, 8, 10, 12 })
        {
            Assert.Equal(bitDepth, new AvifEncoder { BitDepth = bitDepth }.BitDepth);
        }
    }

    [Theory]
    [InlineData("image.avif")]
    [InlineData("IMAGE.AVIF")]
    [InlineData("image.avifs")]
    public void PathSavesInferAvifFromTheExtension(string name)
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-avif-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1, transparent: false);
            var path = directory / name;
            image.Save(path);
            var reader = ReferenceAvif.Parse(File.ReadAllBytes(path));
            Assert.Equal("avif", reader.MajorBrand);
            Assert.Equal(ImageFormat.Avif, Image.Identify(path).Format);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 5)]
    [InlineData(64, 64)]
    [InlineData(70, 33)]
    public void LosslessOutputPreservesEverySampleOfAnOpaqueImage(int width, int height)
    {
        using var image = CreateImage(width, height, seed: width + height, transparent: false);
        var data = Encode(image, new AvifEncoder());
        var reader = ReferenceAvif.Parse(data);

        // The structure: one AV1 item of the High profile, 8-bit 4:4:4 with the identity matrix in full range
        Assert.Equal("avif", reader.MajorBrand);
        Assert.Contains("mif1", reader.CompatibleBrands);
        Assert.Contains("miaf", reader.CompatibleBrands);
        Assert.Contains("MA1A", reader.CompatibleBrands);
        Assert.False(reader.IsSequence);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal("av01", item.Type);
        Assert.Equal((width, height), item.Size);
        Assert.Equal([8, 8, 8], item.PixelInformation);
        Assert.Equal(new ReferenceAvif.Av1Configuration(SequenceProfile: 1, Av1Levels.GetLevelIndex(width, height), BitDepth: 8, IsMonochrome: false, SubsamplingX: 0, SubsamplingY: 0, ChromaSamplePosition: 0), item.Configuration);
        Assert.Equal(new ReferenceAvif.ColorDescription(2, 2, 0, FullRange: true), item.Nclx);
        Assert.Null(item.IccProfile);
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
        Assert.Empty(item.Transforms);
        Assert.True(item.Associations.Single(association => association.Property.Type is "av1C").IsEssential);
        Assert.Equal(["ftyp", "meta", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));

        // The item is the end of the file
        var (offset, length) = Assert.Single(item.Extents);
        Assert.Equal(data.Length, offset + length);

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
        Assert.Equal(new CicpColorDescription(2, 2, 0, fullRange: true), decoded.Metadata.Cicp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlphaIsALosslessAuxiliaryImageThatKeepsHiddenColorsUnlessClearingIsRequested(bool clear)
    {
        using var image = CreateImage(19, 11, seed: 2, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new AvifEncoder { ClearTransparentColors = clear });
        var reader = ReferenceAvif.Parse(data);
        var color = reader.PrimaryItem;
        var alpha = reader.AlphaItem;
        Assert.NotNull(color);
        Assert.NotNull(alpha);
        Assert.Equal("av01", alpha.Type);
        Assert.Equal(ReferenceAvif.AlphaAuxiliaryType, alpha.AuxiliaryType);
        Assert.Equal((19, 11), alpha.Size);
        Assert.Equal([8], alpha.PixelInformation);
        Assert.True(alpha.Configuration!.IsMonochrome);
        Assert.Equal(8, alpha.Configuration.BitDepth);
        Assert.HasCount(2, reader.Items);
        var reference = Assert.Single(reader.ItemReferences);
        Assert.Equal(("auxl", alpha.Id, color.Id), (reference.Type, reference.FromItemId, Assert.Single(reference.ToItemIds)));

        // The alpha plane precedes the color image, and both are in the media data
        Assert.True(Assert.Single(alpha.Extents).Offset < Assert.Single(color.Extents).Offset);

        using var decoded = Image.Load<Rgba32>(data);
        var actual = GetBytes(decoded);
        for (var i = 0; i < source.Length; i += 4)
        {
            var expected = source.AsSpan(i, 4).ToArray();
            if (clear && expected[3] is 0)
                expected = [0, 0, 0, 0];

            Assert.Equal(expected, actual.AsSpan(i, 4).ToArray());
        }
    }

    [Fact]
    public void AnOpaqueImageOfAPixelFormatWithAlphaHasNoAlphaItem()
    {
        using var image = CreateImage(9, 9, seed: 3, transparent: false);
        var reader = ReferenceAvif.Parse(Encode(image, new AvifEncoder()));
        Assert.Null(reader.AlphaItem);
        Assert.Single(reader.Items);
    }

    [Fact]
    public void GrayPixelsAreAMonochromeImage()
    {
        var pixels = new Gray8[13 * 6];
        var random = new Random(4);
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)random.Next(256));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, 13, 6);
        var data = Encode(image, new AvifEncoder());
        var item = ReferenceAvif.Parse(data).PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal([8], item.PixelInformation);
        Assert.True(item.Configuration!.IsMonochrome);
        Assert.Equal(0, item.Configuration.SequenceProfile);
        Assert.Contains("MA1B", ReferenceAvif.Parse(data).CompatibleBrands);

        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        var actual = new byte[pixels.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        Assert.Equal(pixels.Select(pixel => pixel.Value), actual);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public void SixteenBitPixelsNeedABitDepthAndKeepTheSamplesOfThatDepth(int bitDepth)
    {
        // Samples of the bit depth scaled to 16 bits, as the decoder produces them: they are coded back exactly
        var max = (1 << bitDepth) - 1;
        var random = new Random(bitDepth);
        var pixels = new Rgba64[11 * 7];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba64(Scale(random.Next(max + 1)), Scale(random.Next(max + 1)), Scale(random.Next(max + 1)), Scale(i % 3 is 0 ? random.Next(max + 1) : max));
        }

        using var image = Image.ImportPixelData<Rgba64>(pixels, 11, 7);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new AvifEncoder()));
        Assert.Equal((ImageFormat.Avif, "Bit depth reduction"), (exception.Format, exception.Feature));

        var data = Encode(image, new AvifEncoder { BitDepth = bitDepth });
        var reader = ReferenceAvif.Parse(data);
        Assert.Equal(bitDepth, reader.PrimaryItem!.Configuration!.BitDepth);
        Assert.Equal(bitDepth, reader.AlphaItem!.Configuration!.BitDepth);
        Assert.Equal(bitDepth is 12 ? 2 : 1, reader.PrimaryItem.Configuration.SequenceProfile);
        using var decoded = Image.Load<Rgba64>(data);
        var actual = new Rgba64[pixels.Length];
        decoded.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);

        // Without a bit depth, the consent to reduce the samples selects 12 bits
        var reduced = ReferenceAvif.Parse(Encode(image, new AvifEncoder { AllowBitDepthReduction = true }));
        Assert.Equal(12, reduced.PrimaryItem!.Configuration!.BitDepth);

        ushort Scale(int value) => (ushort)(((value * 65535L) + (max / 2)) / max);
    }

    [Fact]
    public void EightBitPixelsCanBeWrittenWithMoreBitsWithoutLoss()
    {
        using var image = CreateImage(8, 5, seed: 6, transparent: true);
        var data = Encode(image, new AvifEncoder { BitDepth = 10 });
        Assert.Equal(10, ReferenceAvif.Parse(data).PrimaryItem!.Configuration!.BitDepth);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Fact]
    public void AStillImageIsWrittenToANonSeekableStreamWithTheSameBytes()
    {
        using var image = CreateImage(33, 17, seed: 7, transparent: true);
        var seekable = Encode(image, new AvifEncoder());
        using var stream = new TestOutputStream();
        image.Save(stream, new AvifEncoder());
        Assert.Equal(seekable, stream.ToArray());
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        using var image = CreateImage(40, 23, seed: 8, transparent: true);
        Assert.Equal(Encode(image, new AvifEncoder()), Encode(image, new AvifEncoder()));
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft, 0, -1)]
    [InlineData(ExifOrientation.TopRight, 0, 1)]
    [InlineData(ExifOrientation.BottomRight, 2, -1)]
    [InlineData(ExifOrientation.BottomLeft, 0, 0)]
    [InlineData(ExifOrientation.LeftTop, 1, 0)]
    [InlineData(ExifOrientation.RightTop, 3, -1)]
    [InlineData(ExifOrientation.RightBottom, 3, 0)]
    [InlineData(ExifOrientation.LeftBottom, 1, -1)]
    public void TheOrientationIsARotationThenAMirroring(ExifOrientation orientation, int angle, int axis)
    {
        // irot rotates anti-clockwise by quarter turns; imir then exchanges the top and the bottom (0) or the left and the
        // right (1) of the rotated image (ISO/IEC 23008-12, sections 6.5.10 and 6.5.12)
        Assert.Equal((angle, axis), AvifFileWriter.GetTransformations(orientation));

        using var image = CreateImage(6, 4, seed: 9, transparent: true);
        image.Metadata.Orientation = orientation;
        var data = Encode(image, new AvifEncoder());
        var reader = ReferenceAvif.Parse(data);
        var item = reader.PrimaryItem;
        Assert.NotNull(item);
        Assert.Equal((int)orientation, item.Orientation);
        var expected = new List<string>();
        if (angle is not 0)
            expected.Add("irot");

        if (axis >= 0)
            expected.Add("imir");

        Assert.Equal(expected, item.Transforms.Select(property => property.Type));
        Assert.All(item.Associations.Where(association => association.Property.Type is "irot" or "imir"), association => Assert.True(association.IsEssential));

        // The alpha plane has the transformations of the image it belongs to
        Assert.Equal(expected, reader.AlphaItem!.Transforms.Select(property => property.Type));
        if (angle is not 0)
            Assert.Equal(angle, item.Find("irot")!.Payload.Span[0]);

        if (axis >= 0)
            Assert.Equal(axis, item.Find("imir")!.Payload.Span[0]);

        // No EXIF block is created to hold the orientation, and the pixels are not transformed
        Assert.Empty(reader.FindDescriptions(item.Id));
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(orientation, decoded.Metadata.Orientation);
        Assert.Equal(GetBytes(image), GetBytes(decoded));
    }

    [Theory]
    [InlineData(AvifChromaSubsampling.Auto, 1, 1, 0)]
    [InlineData(AvifChromaSubsampling.Ratio420, 1, 1, 0)]
    [InlineData(AvifChromaSubsampling.Ratio422, 1, 0, 2)]
    [InlineData(AvifChromaSubsampling.Ratio444, 0, 0, 1)]
    public void LossyOutputIsYCbCrWithTheRequestedSubsamplingAndExactAlpha(AvifChromaSubsampling subsampling, int subsamplingX, int subsamplingY, int profile)
    {
        using var image = CreateSmoothImage(75, 41, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 90, ChromaSubsampling = subsampling });
        var reader = ReferenceAvif.Parse(data);
        var color = reader.PrimaryItem;
        Assert.NotNull(color);
        Assert.Equal(new ReferenceAvif.Av1Configuration(profile, Av1Levels.GetLevelIndex(75, 41), BitDepth: 8, IsMonochrome: false, subsamplingX, subsamplingY, ChromaSamplePosition: 0), color.Configuration);

        // BT.601 in full range, with the primaries and the transfer function left unspecified
        Assert.Equal(new ReferenceAvif.ColorDescription(2, 2, 6, FullRange: true), color.Nclx);
        Assert.NotNull(reader.AlphaItem);

        using var decoded = Image.Load<Rgba32>(data);
        var actual = GetBytes(decoded);
        double squaredError = 0;
        for (var i = 0; i < source.Length; i += 4)
        {
            Assert.Equal(source[i + 3], actual[i + 3]);
            for (var channel = 0; channel < 3; channel++)
            {
                double difference = source[i + channel] - actual[i + channel];
                squaredError += difference * difference;
            }
        }

        var psnr = 10 * Math.Log10(255.0 * 255.0 * (source.Length / 4 * 3) / Math.Max(squaredError, 1));
        Assert.True(psnr >= 34, string.Create(CultureInfo.InvariantCulture, $"PSNR {psnr:F2} dB"));
    }

    [Fact]
    public void QualityTradesSizeForFidelity()
    {
        using var image = CreateSmoothImage(96, 64, transparent: false);
        var source = GetBytes(image);
        var previousSize = int.MaxValue;
        var previousError = -1.0;
        foreach (var quality in new[] { 100, 80, 50, 20, 0 })
        {
            var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality });
            using var decoded = Image.Load<Rgba32>(data);
            var actual = GetBytes(decoded);
            double error = 0;
            for (var i = 0; i < source.Length; i++)
            {
                error += Math.Abs(source[i] - actual[i]);
            }

            Assert.HasCountLessThan(previousSize, data);
            Assert.True(error > previousError, string.Create(CultureInfo.InvariantCulture, $"quality {quality}: error {error}, the previous quality {previousError}"));
            previousSize = data.Length;
            previousError = error;
        }
    }

    [Fact]
    public void LossyGrayPixelsAreAMonochromeImage()
    {
        var pixels = new Gray8[40 * 30];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Gray8((byte)(((i % 40) * 4) + (i / 40 * 2)));
        }

        using var image = Image.ImportPixelData<Gray8>(pixels, 40, 30);
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 95 });
        var item = ReferenceAvif.Parse(data).PrimaryItem;
        Assert.True(item!.Configuration!.IsMonochrome);
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        var actual = new byte[pixels.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.True(Math.Abs(pixels[i].Value - actual[i]) <= 6, $"pixel {i}: {pixels[i].Value} was decoded as {actual[i]}");
        }
    }

    [Fact]
    public void TheMatrixAndTheRangeOfTheColorDescriptionAreKeptByLossyOutput()
    {
        using var image = CreateSmoothImage(32, 32, transparent: false);
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 9, fullRange: false);
        var data = Encode(image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = 90, BitDepth = 10 });
        Assert.Equal(new ReferenceAvif.ColorDescription(9, 16, 9, FullRange: false), ReferenceAvif.Parse(data).PrimaryItem!.Nclx);
        using var decoded = Image.Load(data);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), decoded.Metadata.Cicp);

        // The lossless coding has no matrix: the description keeps the primaries and the transfer function
        var lossless = Encode(image, new AvifEncoder());
        Assert.Equal(new ReferenceAvif.ColorDescription(9, 16, 0, FullRange: true), ReferenceAvif.Parse(lossless).PrimaryItem!.Nclx);
    }

    [Theory]
    [InlineData(AvifCompression.Lossless, false, null)]
    [InlineData(AvifCompression.Lossless, true, 3)]
    [InlineData(AvifCompression.Lossy, true, 1)]
    public void AnAnimationIsAnImageSequenceWithExactDurationsAndPlayCount(AvifCompression compression, bool transparent, int? totalPlays)
    {
        FrameDuration[] durations = [new(1, 30), new(1, 25), new(0, 1), new(7, 3), new(1, 25)];
        using var image = CreateSmoothImage(40, 26, transparent);
        for (var i = 1; i < durations.Length; i++)
        {
            using var frame = CreateSmoothImage(40, 26, transparent, phase: i * 5);
            image.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < durations.Length; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i];
        }

        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var data = Encode(image, new AvifEncoder { Compression = compression, Quality = 95 });
        var reader = ReferenceAvif.Parse(data);
        Assert.Equal("avis", reader.MajorBrand);
        Assert.True(reader.IsSequence);
        Assert.Contains("avif", reader.CompatibleBrands);
        Assert.Contains("msf1", reader.CompatibleBrands);
        Assert.False(reader.IsFragmented);

        // The boxes of the first image, the moov box in the room reserved for it, then the media data
        Assert.Equal(["ftyp", "meta", "moov", "free", "mdat"], reader.TopLevelBoxes.Select(box => box.Type));
        Assert.Equal(data.Length, reader.TopLevelBoxes[^1].Offset + reader.TopLevelBoxes[^1].Size);
        Assert.True(reader.TopLevelBoxes[^1].HasLargeSize);

        // The time scale is the smallest one that holds 1/30, 1/25, 0 and 7/3 exactly: 150
        var track = reader.ColorTrack;
        Assert.NotNull(track);
        Assert.Equal(150u, track.Timescale);
        Assert.Equal([5u, 6u, 0u, 350u, 6u], track.Samples.Select(sample => sample.Duration));
        Assert.Equal("av01", track.SampleEntryType);
        Assert.Equal((40, 26), (track.Width, track.Height));
        Assert.Equal(totalPlays, track.TotalPlays);
        Assert.Equal("co64", track.ChunkOffsetBox);
        var alpha = reader.AlphaTrack;
        if (transparent)
        {
            Assert.NotNull(alpha);
            Assert.Equal(ReferenceAvif.AlphaAuxiliaryType, alpha.AuxiliaryType);
            Assert.HasCount(durations.Length, alpha.Samples);
            Assert.Equal(("auxl", 1u), (Assert.Single(alpha.References).Type, Assert.Single(Assert.Single(alpha.References).TrackIds)));
        }
        else
        {
            // An opaque image of a pixel format with alpha still has an alpha track in a sequence
            Assert.NotNull(alpha);
        }

        // The samples are in the media data, one after the other, and the first image is also the primary item
        long position = track.Samples[0].Offset - (alpha?.Samples[0].Size ?? 0);
        for (var i = 0; i < durations.Length; i++)
        {
            if (alpha is not null)
            {
                Assert.Equal(position, alpha.Samples[i].Offset);
                position += alpha.Samples[i].Size;
            }

            Assert.Equal(position, track.Samples[i].Offset);
            position += track.Samples[i].Size;
        }

        Assert.Equal(data.Length, position);
        Assert.Equal((track.Samples[0].Offset, (long)track.Samples[0].Size), Assert.Single(reader.PrimaryItem!.Extents));

        using var decoded = Image.Load<Rgba32>(data);
        Assert.HasCount(durations.Length, decoded.Frames);
        Assert.Equal(totalPlays, decoded.Animation?.TotalPlays);
        for (var i = 0; i < durations.Length; i++)
        {
            Assert.Equal(durations[i], decoded.Frames[i].Metadata.Duration);
            var expected = GetBytes(image, i);
            var actual = GetBytes(decoded, i);
            if (compression is AvifCompression.Lossless)
            {
                Assert.Equal(expected, actual);
            }
            else
            {
                for (var j = 0; j < expected.Length; j++)
                {
                    Assert.True(Math.Abs(expected[j] - actual[j]) <= (j % 4 is 3 ? 0 : 24), $"frame {i}, byte {j}: {expected[j]} was decoded as {actual[j]}");
                }
            }
        }
    }

    [Fact]
    public void AnImageSequenceNeedsASeekableStreamAndTheNumberOfItsFrames()
    {
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        using var stream = new TestOutputStream();
        Assert.Equal("stream", Assert.Throws<ArgumentException>(() => image.Save(stream, new AvifEncoder())).ParamName);
        Assert.Equal(0, stream.BytesWritten);

        using var seekable = new MemoryStream();
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(seekable, new ImageWriterOptions(8, 8) { Encoder = new AvifEncoder() }));
    }

    [Fact]
    public void DurationsWithoutACommonTimeScaleNeedARoundingPolicy()
    {
        // 1/4294967291 and 1/4294967279 (two primes below 2^32) have no common time scale of 32 bits
        using var image = CreateSmoothImage(8, 8, transparent: false);
        using (var second = CreateSmoothImage(8, 8, transparent: false, phase: 3))
        {
            image.AppendFrame(second.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 4294967291);
        image.Frames[1].Metadata.Duration = new FrameDuration(1073741820, 4294967279);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new AvifEncoder()));
        Assert.Equal("AVIF frame duration precision", exception.Feature);
        Assert.Equal(0, stream.Length);

        // Rounded to microseconds: 0 and 250,000 (0.25 s)
        var data = Encode(image, new AvifEncoder { DurationRounding = FrameDurationRounding.RoundToNearest });
        var track = ReferenceAvif.Parse(data).ColorTrack;
        Assert.Equal(1_000_000u, track!.Timescale);
        Assert.Equal([0u, 250_000u], track.Samples.Select(sample => sample.Duration));
    }

    private static Image<Rgba32> CreateSmoothImage(int width, int height, bool transparent, int phase = 0)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(128 + (100 * Math.Sin((x + phase) / 11.0)));
                var g = (byte)(128 + (100 * Math.Cos((x + y) / 17.0)));
                var b = (byte)((x * 255 / width / 2) + (y * 255 / height / 2));
                pixels[(y * width) + x] = new Rgba32(r, g, b, transparent ? (byte)((x * 7) ^ (y * 13)) : byte.MaxValue);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static byte[] Encode(Image image, AvifEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);

        // TEMPORARY-DUMP
        if (Environment.GetEnvironmentVariable("AV1_ENC_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            var name = Path.Combine(dump, $"{TestContext.Current.Test?.TestDisplayName.GetHashCode(StringComparison.Ordinal):x8}-{Directory.GetFiles(dump).Length}");
            File.WriteAllBytes(name + ".avif", stream.ToArray());
            using var png = new MemoryStream();
            image.Save(png, new PngEncoder());
            File.WriteAllBytes(name + ".png", png.ToArray());
        }

        return stream.ToArray();
    }

    private static byte[] GetBytes(Image image, int frame = 0)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[frame].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static Image<Rgba32> CreateImage(int width, int height, int seed, bool transparent)
    {
        ReadOnlySpan<byte> alphas = [0, 255, 128, 1, 254];
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = transparent ? alphas[i % alphas.Length] : (byte)255;
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }
}
