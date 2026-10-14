using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Jpeg2000;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// JPEG 2000 with the pinned reference tools: OpenJPEG (<c>opj_compress</c>, <c>opj_decompress</c>), OpenJPH
/// (<c>ojph_compress</c>) and the native JPEG 2000 decoder of FFmpeg, which shares no code with OpenJPEG.
/// <list type="bullet">
/// <item><description>the lossless output of the encoder, for every corpus reference in every pixel format that holds it, decodes to exactly the source samples in OpenJPEG and in FFmpeg, hidden colors included;</description></item>
/// <item><description>the lossy output is read alike by OpenJPEG, FFmpeg and the library, keeps the opacity exactly, and its error follows the quality;</description></item>
/// <item><description>the metadata of a JP2 file is where the independent reader of the harness and FFmpeg find it;</description></item>
/// <item><description>the decoder gives the samples OpenJPEG gives for codestreams written by OpenJPEG and by OpenJPH with options drawn at random from a seed.</description></item>
/// </list>
/// </summary>
public sealed class Jpeg2000InteropTests
{
    public static TheoryData<string, string> EncoderCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 48)];

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task LosslessOutputDecodesExactlyInOpenJpegAndFFmpeg(string id, string format)
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var ffmpegFormat = GetFFmpegFormat(pixelFormat);

        // Both containers are exercised across the corpus; the container of a case only depends on its name
        var container = (id.Sum(c => c) + (int)pixelFormat) % 2 == 0 ? Jpeg2000Container.Jp2 : Jpeg2000Container.Codestream;
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LosslessOutputDecodesExactlyInOpenJpegAndFFmpeg));
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var path = directory / $"{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}{(container == Jpeg2000Container.Jp2 ? ".jp2" : ".j2k")}";
            await image.SaveAsync(path, new Jpeg2000Encoder { Container = container, MetadataHandling = MetadataHandling.Strip }, XunitCancellationToken);
            var context = $"{id} {name} as {format} ({container})";

            // opj_decompress, the channels of the file in order, at its depth
            var channels = await openJpeg.DecodeComponentsAsync(path, XunitCancellationToken);
            AssertChannels(source, channels, tolerance: 0, $"{context}: OpenJPEG {openJpeg.Version} decodes different samples.");

            var decoded = await ffmpeg.DecodeToRawFramesAsync(path, ffmpegFormat, source.Width, source.Height, XunitCancellationToken);
            Assert.True(source.Span.SequenceEqual(Assert.Single(decoded)), $"{context}: FFmpeg {ffmpeg.Version} decodes different samples.");
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgb24, 0)]
    [InlineData(PixelFormat.Rgba32, 1)]
    [InlineData(PixelFormat.Gray8, 2)]
    [InlineData(PixelFormat.Gray16, 3)]
    [InlineData(PixelFormat.Rgba64, 4)]
    public async Task LossyOutputIsReadAlikeAndItsErrorFollowsTheQuality(PixelFormat format, int seed)
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var layout = ImageSnapshots.GetLayout(format);

        // More than one code-block per sub-band, and five decomposition levels
        var source = CreatePattern(150, 97, layout, seed);
        using var image = EncoderSources.CreateImage(source, format);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(LossyOutputIsReadAlikeAndItsErrorFollowsTheQuality));
        var previousError = double.MaxValue;
        var previousLength = 0L;
        foreach (var quality in new[] { 20, 50, 80, 95, 100 })
        {
            var container = quality == 50 ? Jpeg2000Container.Codestream : Jpeg2000Container.Jp2;
            var path = directory / $"lossy-{format}-q{quality}{(container == Jpeg2000Container.Jp2 ? ".jp2" : ".j2k")}";
            await image.SaveAsync(path, new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossy, Quality = quality, Container = container }, XunitCancellationToken);
            var context = $"{format} at quality {quality}";

            // The irreversible transformation, and for a color image the irreversible component transformation; the
            // opacity is coded with the reversible one
            var structure = ReferenceJpeg2000.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.False(structure.CodingStyles[0].IsReversible, context);
            Assert.Equal(layout.IsColor, structure.UsesComponentTransform);
            Assert.Equal(layout.HasAlpha, structure.CodingStyles.Any(style => style.Component == layout.ChannelCount - 1 && style.IsReversible));

            // The library reads its output; the reference decoders read the same samples, within one level of 8 bits
            using var decoded = await Image.LoadAsync(path, cancellationToken: XunitCancellationToken);
            var library = ImageSnapshots.Capture(decoded).Frames[0].Pixels;
            Assert.Equal(layout, library.Layout);
            var unit = layout.ScaleFrom8Bit;
            var channels = await openJpeg.DecodeComponentsAsync(path, XunitCancellationToken);
            AssertChannels(library, channels, tolerance: unit, $"{context}: OpenJPEG {openJpeg.Version} and the library decode different samples.");
            var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, GetFFmpegFormat(format), source.Width, source.Height, XunitCancellationToken));
            var ffmpegPixels = RawPixelBuffer.Create(source.Width, source.Height, layout, frame);
            Assert.True(GetMaxDifference(library, ffmpegPixels, layout.ChannelCount) <= unit, $"{context}: FFmpeg {ffmpeg.Version} and the library decode different samples.");

            // The opacity is exact; the error of the colors and the size of the file follow the quality
            if (layout.HasAlpha)
            {
                for (var y = 0; y < source.Height; y++)
                {
                    for (var x = 0; x < source.Width; x++)
                    {
                        Assert.Equal(source.GetSample(x, y, layout.AlphaChannel), library.GetSample(x, y, layout.AlphaChannel));
                    }
                }
            }

            var error = GetMeanDifference(source, library, layout.HasAlpha ? layout.ChannelCount - 1 : layout.ChannelCount) / unit;
            var length = new FileInfo(path).Length;
            Assert.True(error <= previousError && length >= previousLength && (error > 0 || quality == 100), string.Create(CultureInfo.InvariantCulture, $"{context}: mean error {error:0.###} (was {previousError:0.###}), {length} bytes (was {previousLength})."));
            previousError = error;
            previousLength = length;
        }

        // The best quality is close to the source, or is the source
        Assert.True(previousError < 1.5, string.Create(CultureInfo.InvariantCulture, $"{format}: mean error {previousError:0.###} at quality 100."));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOfAFileIsReadByTheReferenceToolsAndTheSamplesAreNotChanged(bool sixteenBit)
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var format = sixteenBit ? PixelFormat.Rgba64 : PixelFormat.Rgb24;
        var layout = ImageSnapshots.GetLayout(format);
        var source = CreatePattern(17, 9, layout, seed: 2);
        var xmp = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'/></x:xmpmeta>"u8.ToArray();
        using var image = EncoderSources.CreateImage(source, format);
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Orientation = ExifOrientation.RightTop;
        image.Metadata.Resolution = new ImageResolution(72.009, 144.018);
        var path = InteropSettings.GetArtifactsDirectory(nameof(MetadataOfAFileIsReadByTheReferenceToolsAndTheSamplesAreNotChanged)) / $"metadata-{format}.jp2";
        await image.SaveAsync(path, new Jpeg2000Encoder(), XunitCancellationToken);

        // The independent reader of the harness: the boxes, the resolution as a number of grid points per meter, the XMP
        // packet byte for byte and an EXIF profile that carries the orientation
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var file = ReferenceJpeg2000.Parse(data);
        Assert.True(file.IsFile);
        Assert.Equal("jp2 ", file.Brand);
        Assert.Equal((1, 16), (file.ColorMethod, file.EnumeratedColorSpace));
        Assert.NotNull(file.CaptureResolution);
        var resolution = file.CaptureResolution.Value;
        Assert.Equal(5670m, resolution.VerticalNumerator * Power(resolution.VerticalExponent) / resolution.VerticalDenominator);
        Assert.Equal(2835m, resolution.HorizontalNumerator * Power(resolution.HorizontalExponent) / resolution.HorizontalDenominator);
        Assert.True(file.Xmp is { } packet && packet.Span.SequenceEqual(xmp));
        Assert.NotNull(file.Exif);
        Assert.Equal(6, ReadExifOrientation(file.Exif.Value.Span));
        Assert.Equal(sixteenBit ? [new(0, 0, 1), new(1, 0, 2), new(2, 0, 3), new(3, 1, 0)] : Array.Empty<ReferenceJp2Channel>(), file.ChannelDefinitions);

        // The orientation is metadata: the reference decoders return the stored samples
        AssertChannels(source, await openJpeg.DecodeComponentsAsync(path, XunitCancellationToken), tolerance: 0, $"OpenJPEG {openJpeg.Version} decodes different samples.");
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, GetFFmpegFormat(format), source.Width, source.Height, XunitCancellationToken);
        Assert.True(source.Span.SequenceEqual(Assert.Single(decoded)), $"FFmpeg {ffmpeg.Version} decodes different samples.");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task CodestreamsOfOpenJpegDecodeToTheSamplesOpenJpegDecodes(int seed)
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();
        var random = new SeededRandom(seed);

        // An image and options drawn from the seed: every tool of the codestream meets every other one over the seeds
        var componentCount = Pick(random, 1, 3, 3, 4);
        var deep = random.Next(4) == 0;
        var width = random.Next(1, 90);
        var height = random.Next(1, 70);
        var layout = (componentCount, deep) switch
        {
            (1, false) => RawPixelLayout.Gray8,
            (1, true) => RawPixelLayout.Gray16Le,
            (3, false) => RawPixelLayout.Rgb8,
            (_, false) => RawPixelLayout.Rgba8,
            _ => RawPixelLayout.Rgba16Le,
        };
        var source = CreatePattern(width, height, layout, seed);
        var irreversible = random.Next(3) == 0;
        var levels = random.Next(0, Math.Min(6, System.Numerics.BitOperations.Log2((uint)Math.Min(width, height)) + 1));
        var arguments = new List<string> { "-n", (levels + 1).ToString(CultureInfo.InvariantCulture), "-p", Pick(random, "LRCP", "RLCP", "RPCL", "PCRL", "CPRL") };
        if (irreversible)
        {
            arguments.Add("-I");
        }

        arguments.AddRange(["-r", Pick(random, "1", "4,1", "20,8,1", irreversible ? "6" : "1", "3")]);
        arguments.AddRange(["-b", Pick(random, "64,64", "32,32", "16,16", "8,8", "4,4", "64,4", "4,32")]);
        arguments.AddRange(["-M", Pick(random, 0, 0, 1, 2, 4, 8, 16, 32, 63, 5, 42).ToString(CultureInfo.InvariantCulture)]);
        if (random.Next(3) == 0)
        {
            arguments.AddRange(["-c", Pick(random, "[16,16],[8,8]", "[32,32]", "[64,64],[32,32],[16,16]")]);
        }

        if (random.Next(3) == 0)
        {
            arguments.AddRange(["-t", Pick(random, "16,16", "24,12", "37,29", "8,40")]);
            if (random.Next(2) == 0)
            {
                arguments.AddRange(["-TP", Pick(random, "R", "L", "C")]);
            }
        }

        if (random.Next(4) == 0)
        {
            arguments.AddRange(["-d", Pick(random, "3,2", "5,7", "1,1"), "-T", "1,1"]);
        }

        if (random.Next(3) == 0)
        {
            arguments.Add("-SOP");
        }

        if (random.Next(3) == 0)
        {
            arguments.Add("-EPH");
        }

        if (componentCount >= 3 && random.Next(3) == 0)
        {
            arguments.AddRange(["-mct", "0"]);
        }

        if (componentCount == 1 && random.Next(4) == 0)
        {
            arguments.AddRange(["-ROI", "c=0,U=" + random.Next(1, 8).ToString(CultureInfo.InvariantCulture)]);
        }

        var directory = InteropSettings.GetArtifactsDirectory(nameof(CodestreamsOfOpenJpegDecodeToTheSamplesOpenJpegDecodes));
        var input = directory / $"seed-{seed}{(componentCount == 1 ? ".pgm" : componentCount == 3 ? ".ppm" : ".pam")}";
        var output = directory / $"seed-{seed}.j2k";
        await File.WriteAllBytesAsync(input, WriteNetpbm(source), XunitCancellationToken);
        var context = $"seed {seed}: {width}x{height} {layout.Name}, opj_compress {string.Join(' ', arguments)}";
        var result = await openJpeg.TryEncodeAsync(input, output, arguments, XunitCancellationToken);
        if (result.ExitCode != 0)
        {
            // The encoder refuses some combinations (more resolution levels than a tile of that size has): the plain
            // codestream of the same image is compared instead
            arguments = ["-n", "1"];
            await openJpeg.EncodeAsync(input, output, arguments, XunitCancellationToken);
            context = $"seed {seed}: {width}x{height} {layout.Name}, opj_compress -n 1";
        }

        var reference = await openJpeg.DecodeComponentsAsync(output, XunitCancellationToken);
        using var image = await Image.LoadAsync(output, cancellationToken: XunitCancellationToken);
        var decoded = ImageSnapshots.Capture(image).Frames[0].Pixels;
        Assert.Equal(layout, decoded.Layout);

        // Integer arithmetic for the reversible transformations, whatever the coding passes that are left out; real
        // numbers for the irreversible ones
        var isExact = !arguments.Contains("-I", StringComparer.Ordinal);
        AssertChannels(decoded, reference, isExact ? 0 : layout.ScaleFrom8Bit, context + $": OpenJPEG {openJpeg.Version} decodes other samples than the library.");
        if (isExact && arguments[arguments.IndexOf("-r") + 1].EndsWith("1", StringComparison.Ordinal))
        {
            Assert.True(source.Span.SequenceEqual(decoded.Span), context + ": a lossless codestream does not decode to the samples given to the encoder.");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task CodestreamsOfOpenJphDecodeToTheSamplesOpenJpegDecodes(int seed)
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();
        var openJph = await RequiredTools.OpenJphAsync();
        var random = new SeededRandom(seed);

        // The high-throughput block coder; ojph_compress reads one or three components. An image without decomposition
        // level loses its samples of value 0 in ojph_compress: at least one level
        var isColor = random.Next(2) == 0;
        var deep = random.Next(4) == 0;
        var width = random.Next(2, 90);
        var height = random.Next(2, 70);
        var layout = (isColor, deep) switch
        {
            (false, false) => RawPixelLayout.Gray8,
            (false, true) => RawPixelLayout.Gray16Le,
            (true, false) => RawPixelLayout.Rgb8,
            _ => RawPixelLayout.Rgba16Le,
        };

        // There is no 16-bit RGB layout: the opacity of the pattern is not given to the encoder
        var source = CreatePattern(width, height, layout, seed);
        var reversible = random.Next(3) != 0;
        var arguments = new List<string>
        {
            "-reversible", reversible ? "true" : "false",
            "-num_decomps", random.Next(1, Math.Min(6, System.Numerics.BitOperations.Log2((uint)Math.Min(width, height)) + 1) + 1).ToString(CultureInfo.InvariantCulture),
            "-prog_order", Pick(random, "LRCP", "RLCP", "RPCL", "PCRL", "CPRL"),
            "-block_size", Pick(random, "{64,64}", "{32,32}", "{16,16}", "{4,4}", "{4,64}", "{32,8}"),
        };
        if (!reversible)
        {
            arguments.AddRange(["-qstep", Pick(random, "0.002", "0.01", "0.05")]);
        }

        if (random.Next(3) == 0)
        {
            arguments.AddRange(["-precincts", Pick(random, "{16,16},{32,32}", "{32,32}", "{64,64},{128,128}")]);
        }

        if (random.Next(3) == 0)
        {
            arguments.AddRange(["-tile_size", Pick(random, "{16,16}", "{24,12}", "{37,29}")]);
            if (random.Next(2) == 0)
            {
                arguments.AddRange(["-tileparts", Pick(random, "R", "C", "RC")]);
            }
        }

        if (isColor && random.Next(3) == 0)
        {
            arguments.AddRange(["-colour_trans", "false"]);
        }

        var directory = InteropSettings.GetArtifactsDirectory(nameof(CodestreamsOfOpenJphDecodeToTheSamplesOpenJpegDecodes));
        var input = directory / $"seed-{seed}{(isColor ? ".ppm" : ".pgm")}";
        var output = directory / $"seed-{seed}.j2c";
        await File.WriteAllBytesAsync(input, WriteNetpbm(source, isColor ? 3 : 1), XunitCancellationToken);
        var context = $"seed {seed}: {width}x{height} {(isColor ? "RGB" : "gray")} of {(deep ? 16 : 8)} bits, ojph_compress {string.Join(' ', arguments)}";
        await openJph.EncodeAsync(input, output, arguments, XunitCancellationToken);
        Assert.NotNull(ReferenceJpeg2000.Parse(await File.ReadAllBytesAsync(output, XunitCancellationToken)).HighThroughputCapabilities);

        var reference = await openJpeg.DecodeComponentsAsync(output, XunitCancellationToken);
        using var image = await Image.LoadAsync(output, cancellationToken: XunitCancellationToken);
        var decoded = ImageSnapshots.Capture(image).Frames[0].Pixels;
        Assert.Equal(layout, decoded.Layout);
        AssertChannels(decoded, reference, reversible ? 0 : layout.ScaleFrom8Bit, context + $": OpenJPEG {openJpeg.Version} decodes other samples than the library.", channelCount: reference.Count);
        if (reversible)
        {
            Assert.Equal(0, GetMaxDifference(source, decoded, reference.Count));
        }
    }

    [Fact]
    public async Task ExternalCodestreamsDecodeToTheSamplesOpenJpegDecodes()
    {
        // An opt-in campaign over files that are not in the repository, for example the conformance codestreams of
        // Rec. ITU-T T.803: every file OpenJPEG decodes must decode to the same samples, or be rejected as unsupported
        var directory = Environment.GetEnvironmentVariable("MEZIANTOU_FRAMEWORK_IMAGING_JPEG2000_DIFF_STREAMS");
        if (string.IsNullOrEmpty(directory))
            return;

        var openJpeg = await RequiredTools.OpenJpegAsync();
        var work = InteropSettings.GetArtifactsDirectory(nameof(ExternalCodestreamsDecodeToTheSamplesOpenJpegDecodes));
        var failures = new List<string>();
        var compared = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (Path.GetExtension(file).ToUpperInvariant() is not (".J2K" or ".J2C" or ".JPC" or ".JP2" or ".JPH" or ".JHC" or ".JPX" or ".JPF"))
                continue;

            var copy = work / Path.GetFileName(file);
            File.Copy(file, copy, overwrite: true);
            var (reference, _) = await openJpeg.TryDecodeComponentsAsync(copy, XunitCancellationToken);
            if (reference is null)
                continue;

            try
            {
                using var image = await Image.LoadAsync(copy, cancellationToken: XunitCancellationToken);
                var decoded = ImageSnapshots.Capture(image).Frames[0].Pixels;
                if (reference.Count != decoded.Layout.ChannelCount || reference.Any(channel => channel.Width != decoded.Width || channel.Height != decoded.Height || channel.IsSigned))
                    continue;

                compared++;
                var structure = ReferenceJpeg2000.Parse(await File.ReadAllBytesAsync(copy, XunitCancellationToken));
                var tolerance = structure.CodingStyles.All(style => style.IsReversible) && structure.EnumeratedColorSpace != 18 ? 0 : decoded.Layout.ScaleFrom8Bit;
                AssertChannels(decoded, reference, tolerance, Path.GetFileName(file));
            }
            catch (UnsupportedImageFeatureException)
            {
            }
            catch (Exception exception) when (exception is ImageException or Meziantou.Framework.Assertions.AssertionException)
            {
                failures.Add($"{Path.GetFileName(file)}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} of {compared} files differ:\n{string.Join('\n', failures)}");
        }
    }

    private static decimal Power(int exponent) => exponent >= 0 ? (decimal)Math.Pow(10, exponent) : 1 / (decimal)Math.Pow(10, -exponent);

    private static T Pick<T>(SeededRandom random, params ReadOnlySpan<T> values) => values[random.Next(values.Length)];

    /// <summary>A linear congruential generator: the cases of a seed are the same on every runtime.</summary>
    private sealed class SeededRandom(int seed)
    {
        private uint _state = ((uint)seed * 2654435761) + 1;

        public int Next(int exclusiveMaximum)
        {
            _state = (_state * 1664525) + 1013904223;
            return (int)((_state >> 8) % (uint)exclusiveMaximum);
        }

        public int Next(int minimum, int exclusiveMaximum) => minimum + Next(exclusiveMaximum - minimum);
    }

    private static string GetFFmpegFormat(PixelFormat format) => format switch
    {
        PixelFormat.Rgb24 => "rgb24",
        PixelFormat.Gray8 => "gray",
        PixelFormat.Gray16 => "gray16le",
        PixelFormat.Rgba64 => "rgba64le",
        _ => "rgba",
    };

    /// <summary>
    /// Compares the channels a reference decoder returns, at the depth of the file, with pixels: a sample of a depth
    /// other than 8 or 16 bits is scaled the way the library scales it.
    /// </summary>
    private static void AssertChannels(RawPixelBuffer expected, IReadOnlyList<PgxImage> channels, int tolerance, string message, int? channelCount = null)
    {
        var layout = expected.Layout;
        var count = channelCount ?? layout.ChannelCount;
        Assert.Equal(count, channels.Count);
        for (var channel = 0; channel < count; channel++)
        {
            var plane = channels[channel];
            Assert.Equal((expected.Width, expected.Height, false), (plane.Width, plane.Height, plane.IsSigned));
            long maximum = (1L << plane.Precision) - 1;
            var samples = plane.Samples.Span;
            for (var y = 0; y < expected.Height; y++)
            {
                for (var x = 0; x < expected.Width; x++)
                {
                    var actual = (int)((((long)samples[(y * expected.Width) + x] * layout.MaxSampleValue) + (maximum / 2)) / maximum);
                    var value = expected.GetSample(x, y, channel);
                    if (Math.Abs(value - actual) > tolerance)
                    {
                        Assert.Fail($"{message} At ({x}, {y}), channel {channel}: {value} and {actual}.");
                    }
                }
            }
        }
    }

    private static int GetMaxDifference(RawPixelBuffer first, RawPixelBuffer second, int channelCount)
    {
        var max = 0;
        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                for (var channel = 0; channel < channelCount; channel++)
                {
                    max = Math.Max(max, Math.Abs(first.GetSample(x, y, channel) - second.GetSample(x, y, channel)));
                }
            }
        }

        return max;
    }

    private static double GetMeanDifference(RawPixelBuffer first, RawPixelBuffer second, int channelCount)
    {
        var sum = 0L;
        for (var y = 0; y < first.Height; y++)
        {
            for (var x = 0; x < first.Width; x++)
            {
                for (var channel = 0; channel < channelCount; channel++)
                {
                    sum += Math.Abs(first.GetSample(x, y, channel) - second.GetSample(x, y, channel));
                }
            }
        }

        return (double)sum / ((long)first.Width * first.Height * channelCount);
    }

    /// <summary>A Netpbm file of the first channels of the pixels: PGM for one, PPM for three, PAM for two or four.</summary>
    private static byte[] WriteNetpbm(RawPixelBuffer pixels, int? channelCount = null)
    {
        var layout = pixels.Layout;
        var channels = channelCount ?? layout.ChannelCount;
        var header = channels switch
        {
            1 => string.Create(CultureInfo.InvariantCulture, $"P5\n{pixels.Width} {pixels.Height}\n{layout.MaxSampleValue}\n"),
            3 => string.Create(CultureInfo.InvariantCulture, $"P6\n{pixels.Width} {pixels.Height}\n{layout.MaxSampleValue}\n"),
            _ => string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {pixels.Width}\nHEIGHT {pixels.Height}\nDEPTH {channels}\nMAXVAL {layout.MaxSampleValue}\nTUPLTYPE {(channels == 2 ? "GRAYSCALE_ALPHA" : "RGB_ALPHA")}\nENDHDR\n"),
        };
        var data = new List<byte>(Encoding.ASCII.GetBytes(header));
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    // Netpbm samples are big-endian
                    var value = pixels.GetSample(x, y, channel);
                    if (layout.BytesPerSample == 2)
                    {
                        data.Add((byte)(value >> 8));
                    }

                    data.Add((byte)value);
                }
            }
        }

        return [.. data];
    }

    private static int ReadExifOrientation(ReadOnlySpan<byte> tiff)
    {
        var littleEndian = tiff[0] == (byte)'I';
        ushort ReadUInt16(ReadOnlySpan<byte> data) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);
        var directory = (int)(littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]));
        var count = ReadUInt16(tiff[directory..]);
        for (var i = 0; i < count; i++)
        {
            var entry = tiff.Slice(directory + 2 + (12 * i), 12);
            if (ReadUInt16(entry) == 0x0112)
                return ReadUInt16(entry[8..]);
        }

        return 1;
    }

    /// <summary>Flat areas, gradients and noise; every channel and position has its own values, hidden colors of transparent pixels included.</summary>
    private static RawPixelBuffer CreatePattern(int width, int height, RawPixelLayout layout, int seed)
    {
        var data = new byte[layout.GetByteLength(width, height)];
        var state = (uint)(seed * 7919) + 1;
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < layout.ChannelCount; channel++)
                {
                    state = (state * 1664525) + 1013904223;
                    var value = (((x / 16) + (y / 16) + seed) % 3) switch
                    {
                        0 => 4000 * (channel + 1 + seed),
                        1 => (x * 211) + (y * 97 * (channel + 1)) + (seed * 1000),
                        _ => (int)(state >> 16),
                    };

                    if (channel == layout.AlphaChannel && (x + y) % 5 == 0)
                    {
                        value = 0;
                    }

                    if (layout.BytesPerSample == 1)
                    {
                        data[offset++] = (byte)value;
                    }
                    else
                    {
                        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)value);
                        offset += 2;
                    }
                }
            }
        }

        return RawPixelBuffer.Create(width, height, layout, data);
    }
}
