using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// JPEG XL encoder output read by the libjxl tools (<c>djxl</c>, <c>jxlinfo</c>), by jxl-oxide, which shares no code with
/// libjxl, and by FFmpeg (through libjxl, a third consumer that is not counted as independent):
/// <list type="bullet">
/// <item><description>the output of every corpus reference, in every pixel format that holds it, decodes to exactly the source samples, hidden colors included;</description></item>
/// <item><description>animations have the frames, the durations and the number of plays that were written;</description></item>
/// <item><description>the ICC profile is extracted byte for byte, the metadata boxes and the color encodings are listed by jxlinfo.</description></item>
/// </list>
/// </summary>
public sealed class JpegXlEncoderInteropTests
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

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task OutputDecodesExactlyInLibJxlJxlOxideAndFFmpeg(string id, string format)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var ffmpegFormat = pixelFormat switch
        {
            PixelFormat.Rgb24 => "rgb24",
            PixelFormat.Gray8 => "gray",
            PixelFormat.Gray16 => "gray16le",
            PixelFormat.Rgba64 => "rgba64le",
            _ => "rgba",
        };

        // Every effort is exercised across the corpus; the effort of a case only depends on its name
        var effort = (id.Sum(c => c) + (int)pixelFormat) % 10;
        var directory = InteropSettings.GetArtifactsDirectory(nameof(JpegXlEncoderInteropTests));
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var path = directory / $"{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}-e{effort}.jxl";
            await image.SaveAsync(path, new JpegXlEncoder { Effort = effort }, XunitCancellationToken);
            var context = $"{id} {name} as {format} (effort {effort})";
            var layout = source.Layout;

            var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
            Assert.Equal((source.Width, source.Height, layout.BytesPerSample * 8, layout.IsColor ? 3 : 1, layout.HasAlpha), (info.Width, info.Height, info.BitsPerSample, info.ColorChannelCount, info.HasAlpha));
            Assert.False(info.IsXybEncoded);
            Assert.False(info.IsAnimation);
            Assert.Equal(layout.BytesPerSample == 2, info.IsContainer);

            // djxl, integer samples at the depth of the file
            var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal((source.Width, source.Height, layout.MaxSampleValue), (reference.Width, reference.Height, reference.MaxValue));
            AssertRgba(source, reference.Rgba.Span, $"{context}: djxl {jxl.Version} decodes different samples.");

            // jxl-oxide, the channels of the file in order
            var frames = await oxide.DecodeFramesAsync(path, XunitCancellationToken);
            Assert.Equal((1, source.Height, source.Width, layout.ChannelCount), (frames.FrameCount, frames.Height, frames.Width, frames.ChannelCount));
            AssertSamples(source, frames.ToIntegerSamples(0, layout.MaxSampleValue, out var distance), $"{context}: jxl-oxide {oxide.Version} decodes different samples.");
            Assert.True(distance < 0.01, $"{context}: jxl-oxide returned samples that are not integers at this depth (distance {distance}).");

            var decoded = await ffmpeg.DecodeToRawFramesAsync(path, ffmpegFormat, source.Width, source.Height, XunitCancellationToken);
            Assert.True(source.Span.SequenceEqual(Assert.Single(decoded)), $"{context}: FFmpeg {ffmpeg.Version} decodes different samples.");
        }
    }

    /// <summary>
    /// Frames of several groups coded with a learned tree, which every group uses from the global data, and its transform:
    /// a palette (screenshots, whose palette is in the global data), a color transform (gradient tiles, noise), copies of
    /// previous values from the effort 5; djxl and jxl-oxide decode exactly the source samples. The global data of a frame
    /// without a palette code no channel, which jxl-oxide reads differently from libjxl when the code uses ANS.
    /// </summary>
    [Theory]
    [InlineData("screenshot", PixelFormat.Rgb24, 3)]
    [InlineData("screenshot", PixelFormat.Rgba32, 5)]
    [InlineData("screenshot", PixelFormat.Gray8, 7)]
    [InlineData("screenshot", PixelFormat.Rgb24, 9)]
    [InlineData("tiles", PixelFormat.Rgb24, 3)]
    [InlineData("tiles", PixelFormat.Rgba64, 5)]
    [InlineData("tiles", PixelFormat.Rgb24, 8)]
    [InlineData("noise", PixelFormat.Rgba32, 4)]
    [InlineData("noise", PixelFormat.Gray16, 7)]
    [InlineData("noise", PixelFormat.Rgb24, 9)]
    public async Task LearnedTreesDecodeExactlyInLibJxlAndJxlOxide(string kind, PixelFormat format, int effort)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var layout = ImageSnapshots.GetLayout(format);
        var source = kind == "noise" ? CreatePattern(600, 300, layout, seed: 6) : CreateContent(kind, 600, 300, layout);
        using var image = EncoderSources.CreateImage(source, format);
        var path = InteropSettings.GetArtifactsDirectory(nameof(LearnedTreesDecodeExactlyInLibJxlAndJxlOxide)) / $"{kind}-{format}-e{effort}.jxl";
        await image.SaveAsync(path, new JpegXlEncoder { Effort = effort }, XunitCancellationToken);
        var context = $"{kind} as {format} (effort {effort})";

        var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
        AssertRgba(source, reference.Rgba.Span, $"{context}: djxl {jxl.Version} decodes different samples.");
        var frames = await oxide.DecodeFramesAsync(path, XunitCancellationToken);
        AssertSamples(source, frames.ToIntegerSamples(0, layout.MaxSampleValue, out _), $"{context}: jxl-oxide {oxide.Version} decodes different samples.");
    }

    [Theory]
    [InlineData(false, PixelFormat.Rgba32, null)]
    [InlineData(true, PixelFormat.Rgba32, 4)]
    [InlineData(false, PixelFormat.Rgb24, 1)]
    [InlineData(true, PixelFormat.Gray8, null)]
    [InlineData(false, PixelFormat.Rgba64, 7)]
    [InlineData(true, PixelFormat.Gray16, null)]
    public async Task AnimationsAreReadFrameByFrame(bool writer, PixelFormat format, int? totalPlays)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();

        // More than one group per frame; a duration that is not a whole number of milliseconds is rounded to the nearest one
        const int Width = 300;
        const int Height = 270;
        FrameDuration[] durations = [new(1, 10), new(7, 1000), new(1, 3), new(2, 1)];
        double[] milliseconds = [100, 7, 333, 2000];
        var layout = ImageSnapshots.GetLayout(format);
        var sources = new RawPixelBuffer[durations.Length];
        for (var i = 0; i < sources.Length; i++)
        {
            sources[i] = CreatePattern(Width, Height, layout, seed: i);
        }

        var path = InteropSettings.GetArtifactsDirectory(nameof(AnimationsAreReadFrameByFrame)) / $"animation-{format}-{(writer ? "writer" : "save")}.jxl";
        using (var image = EncoderSources.CreateImage(sources[0], format))
        {
            for (var i = 1; i < sources.Length; i++)
            {
                using var next = EncoderSources.CreateImage(sources[i], format);
                image.AppendFrame(next.Frames[0]);
            }

            for (var i = 0; i < durations.Length; i++)
            {
                image.Frames[i].Metadata.Duration = durations[i];
            }

            image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
            if (writer)
            {
                // Frames are written as they arrive, to an output that is never sought
                using var stream = new TestOutputStream();
                var options = new ImageWriterOptions(Width, Height) { Encoder = new JpegXlEncoder(), Animation = image.Animation };
                await (format switch
                {
                    PixelFormat.Rgb24 => WriteFramesAsync<Rgb24>(image, stream, options),
                    PixelFormat.Gray8 => WriteFramesAsync<Gray8>(image, stream, options),
                    PixelFormat.Gray16 => WriteFramesAsync<Gray16>(image, stream, options),
                    PixelFormat.Rgba64 => WriteFramesAsync<Rgba64>(image, stream, options),
                    _ => WriteFramesAsync<Rgba32>(image, stream, options),
                });
                await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
            }
            else
            {
                await image.SaveAsync(path, new JpegXlEncoder(), XunitCancellationToken);
            }
        }

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.True(info.IsAnimation);
        Assert.Equal((Width, Height, layout.BytesPerSample * 8, layout.HasAlpha), (info.Width, info.Height, info.BitsPerSample, info.HasAlpha));
        Assert.Equal((1000, 1, totalPlays ?? 0), (info.TicksPerSecondNumerator, info.TicksPerSecondDenominator, info.LoopCount));
        Assert.Equal(milliseconds, info.Frames.Select(frame => frame.DurationMilliseconds ?? -1));
        Assert.All(info.Frames, frame => Assert.Equal((false, true), (frame.IsLayer, frame.IsFullSize)));

        foreach (var (decoder, frames) in new[] { ("djxl", await jxl.DecodeFramesAsync(path, XunitCancellationToken)), ("jxl-oxide", await oxide.DecodeFramesAsync(path, XunitCancellationToken)) })
        {
            Assert.Equal((sources.Length, Height, Width, layout.ChannelCount), (frames.FrameCount, frames.Height, frames.Width, frames.ChannelCount), message: decoder);
            for (var i = 0; i < sources.Length; i++)
            {
                AssertSamples(sources[i], frames.ToIntegerSamples(i, layout.MaxSampleValue, out _), $"{decoder} decodes different samples for the frame {i}.");
            }
        }

        var ffmpegFormat = format switch
        {
            PixelFormat.Rgb24 => "rgb24",
            PixelFormat.Gray8 => "gray",
            PixelFormat.Gray16 => "gray16le",
            PixelFormat.Rgba64 => "rgba64le",
            _ => "rgba",
        };
        var decoded = await ffmpeg.DecodeToRawFramesAsync(path, ffmpegFormat, Width, Height, XunitCancellationToken);
        Assert.Equal(sources.Length, decoded.Count);
        for (var i = 0; i < sources.Length; i++)
        {
            Assert.True(sources[i].Span.SequenceEqual(decoded[i]), $"FFmpeg {ffmpeg.Version} decodes different samples for the frame {i}.");
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    [InlineData(PixelFormat.Gray16)]
    public async Task TheIccProfileIsExtractedByteForByte(PixelFormat format)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var layout = ImageSnapshots.GetLayout(format);
        var source = CreatePattern(21, 13, layout, seed: 4);
        var profile = layout.IsColor
            ? IccProfileBuilder.Rgb(IccProfileBuilder.DisplayP3Colorants, IccProfileBuilder.SrgbCurve())
            : IccProfileBuilder.Gray(IccProfileBuilder.Gamma(563), "XYZ ");
        using var image = EncoderSources.CreateImage(source, format);
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(profile));
        var path = InteropSettings.GetArtifactsDirectory(nameof(TheIccProfileIsExtractedByteForByte)) / $"icc-{format}.jxl";
        await image.SaveAsync(path, new JpegXlEncoder(), XunitCancellationToken);

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal(profile.Length, info.IccProfileLength);
        Assert.Equal(profile, await jxl.GetOriginalIccProfileAsync(path, XunitCancellationToken));

        var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
        AssertRgba(source, reference.Rgba.Span, $"djxl {jxl.Version} decodes different samples.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataBoxesAndTheOrientationAreListed(bool sixteenBit)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var format = sixteenBit ? PixelFormat.Rgba64 : PixelFormat.Rgba32;
        var layout = ImageSnapshots.GetLayout(format);
        var source = CreatePattern(17, 9, layout, seed: 2);
        var xmp = "<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'/></x:xmpmeta>"u8.ToArray();
        using var image = EncoderSources.CreateImage(source, format);
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob([0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        image.Metadata.Orientation = ExifOrientation.BottomRight;
        var path = InteropSettings.GetArtifactsDirectory(nameof(MetadataBoxesAndTheOrientationAreListed)) / $"metadata-{format}.jxl";
        await image.SaveAsync(path, new JpegXlEncoder(), XunitCancellationToken);

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.True(info.IsContainer);
        Assert.Equal(3, info.Orientation);
        string[] expectedBoxes = sixteenBit ? ["JXL ", "ftyp", "jxll", "jxlp", "Exif", "xml ", "jxlp"] : ["JXL ", "ftyp", "jxlp", "Exif", "xml ", "jxlp"];
        Assert.Equal(expectedBoxes, info.Boxes.Select(box => box.Type));
        Assert.Equal(["Exif", "xml "], info.Metadata.Select(box => box.Type));
        Assert.All(info.Metadata, box => Assert.False(box.IsBrotliCompressed));
        Assert.Equal(xmp.Length, info.Boxes.Single(box => box.Type == "xml ").Size - 8);

        // The reference decoders apply the orientation: a rotation by 180 degrees
        var expected = source.Rotate180();
        var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
        AssertRgba(expected, reference.Rgba.Span, $"djxl {jxl.Version} decodes different samples.");
        var frames = await oxide.DecodeFramesAsync(path, XunitCancellationToken);
        AssertSamples(expected, frames.ToIntegerSamples(0, layout.MaxSampleValue, out _), $"jxl-oxide {oxide.Version} decodes different samples.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task EveryOrientationIsTheOneTheReferenceDecoderApplies(int orientation)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var source = CreatePattern(7, 4, RawPixelLayout.Rgb8, seed: orientation);
        using var image = EncoderSources.CreateImage(source, PixelFormat.Rgb24);
        image.Metadata.Orientation = (ExifOrientation)orientation;
        var path = InteropSettings.GetArtifactsDirectory(nameof(EveryOrientationIsTheOneTheReferenceDecoderApplies)) / $"orientation-{orientation}.jxl";
        await image.SaveAsync(path, new JpegXlEncoder(), XunitCancellationToken);

        Assert.Equal(orientation, (await jxl.GetInfoAsync(path, XunitCancellationToken)).Orientation);
        var expected = (ExifOrientation)orientation switch
        {
            ExifOrientation.TopRight => source.FlipHorizontal(),
            ExifOrientation.BottomRight => source.Rotate180(),
            ExifOrientation.BottomLeft => source.FlipVertical(),
            ExifOrientation.LeftTop => source.Transpose(),
            ExifOrientation.RightTop => source.Rotate90Clockwise(),
            ExifOrientation.RightBottom => source.Transverse(),
            ExifOrientation.LeftBottom => source.Rotate90CounterClockwise(),
            _ => source,
        };

        var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
        Assert.Equal((expected.Width, expected.Height), (reference.Width, reference.Height));
        AssertRgba(expected, reference.Rgba.Span, $"djxl {jxl.Version} decodes different samples.");
    }

    [Theory]
    [InlineData(null, null, false, "D65", "sRGB", "Linear")]
    [InlineData(9, 16, false, "D65", "Rec.2100", "PQ")]
    [InlineData(9, 18, false, "D65", "Rec.2100", "HLG")]
    [InlineData(12, 13, false, "D65", "P3", "sRGB")]
    [InlineData(11, 17, false, "P3", "P3", "DCI")] // jxlinfo prints the DCI white point as "P3"
    [InlineData(1, 1, false, "D65", "sRGB", "709")]
    [InlineData(null, null, true, "D65", null, "Linear")]
    [InlineData(1, 16, true, "D65", null, "PQ")]
    public async Task ColorEncodingsAreListed(int? primaries, int? transfer, bool gray, string whitePoint, string? expectedPrimaries, string expectedTransfer)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var format = gray ? PixelFormat.Gray8 : PixelFormat.Rgb24;
        var source = CreatePattern(9, 5, ImageSnapshots.GetLayout(format), seed: 6);
        using var image = EncoderSources.CreateImage(source, format);
        if (primaries is null)
        {
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }
        else
        {
            image.Metadata.Cicp = new CicpColorDescription((byte)primaries, (byte)transfer!, 0, fullRange: true);
        }

        var path = InteropSettings.GetArtifactsDirectory(nameof(ColorEncodingsAreListed)) / $"color-{primaries}-{transfer}-{format}.jxl";
        await image.SaveAsync(path, new JpegXlEncoder(), XunitCancellationToken);

        var info = await jxl.GetInfoAsync(path, XunitCancellationToken);
        Assert.Null(info.IccProfileLength);
        Assert.Equal((gray ? "Grayscale" : "RGB", whitePoint, expectedPrimaries, expectedTransfer), (info.ColorSpace, info.WhitePoint, info.Primaries, info.TransferFunction));

        // The encoding is a label: the samples are the ones that were written
        var reference = await jxl.DecodeAsync(path, XunitCancellationToken);
        AssertRgba(source, reference.Rgba.Span, $"djxl {jxl.Version} decodes different samples.");
    }

    private static async Task WriteFramesAsync<TPixel>(Image image, Stream stream, ImageWriterOptions options)
        where TPixel : unmanaged
    {
        await using var writer = Image.CreateWriter<TPixel>(stream, options);
        foreach (var frame in image.Frames)
        {
            await writer.WriteFrameAsync((ImageFrame<TPixel>)frame, XunitCancellationToken);
        }

        await writer.CompleteAsync(XunitCancellationToken);
    }

    /// <summary>Compares a decoded image expanded to RGBA at its depth (gray replicated, opaque without alpha) with a source.</summary>
    private static void AssertRgba(RawPixelBuffer source, ReadOnlySpan<ushort> rgba, string message)
    {
        var layout = source.Layout;
        Assert.HasCount(source.Width * source.Height * 4, rgba);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    var expected = channel == 3
                        ? (layout.HasAlpha ? source.GetSample(x, y, layout.AlphaChannel) : layout.MaxSampleValue)
                        : source.GetSample(x, y, layout.IsColor ? channel : 0);
                    var actual = rgba[(((y * source.Width) + x) * 4) + channel];
                    if (expected != actual)
                    {
                        Assert.Fail($"{message} At ({x}, {y}), channel {channel}: expected {expected}, actual {actual}.");
                    }
                }
            }
        }
    }

    /// <summary>Compares the samples of a decoded image, in the channels of the file, with a source.</summary>
    private static void AssertSamples(RawPixelBuffer source, int[] samples, string message)
    {
        var channels = source.Layout.ChannelCount;
        Assert.HasCount(source.SampleCount, samples);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var channel = 0; channel < channels; channel++)
                {
                    var expected = source.GetSample(x, y, channel);
                    var actual = samples[(((y * source.Width) + x) * channels) + channel];
                    if (expected != actual)
                    {
                        Assert.Fail($"{message} At ({x}, {y}), channel {channel}: expected {expected}, actual {actual}.");
                    }
                }
            }
        }
    }

    /// <summary>Flat areas, gradients and noise; every channel and position has its own values, hidden colors of transparent pixels included.</summary>
    /// <summary>
    /// A screenshot (glyphs in three colors with four levels of coverage on a light background, buttons, a gradient band) or
    /// tiles of four gradient patterns, as the samples of a layout (8-bit values, scaled to 16 bits).
    /// </summary>
    private static RawPixelBuffer CreateContent(string kind, int width, int height, RawPixelLayout layout)
    {
        var colors = new (int R, int G, int B)[width * height];
        if (kind == "tiles")
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var tile = (((x / 64) * 7) + ((y / 48) * 3)) % 4;
                    var (u, v) = (x % 64, y % 48);
                    var inside = ((u - 32) * (u - 32)) + ((v - 24) * (v - 24)) < 300;
                    colors[(y * width) + x] = inside ? (u * 4, v * 5, tile * 60) : u < 2 || v < 2 ? (40, 40, 40) : (tile * 50, 120, 200 - (tile * 30));
                }
            }
        }
        else
        {
            var state = 12345u;
            int Next(int max)
            {
                state = (state * 1664525) + 1013904223;
                return (int)((state >> 16) % (uint)max);
            }

            var background = (245, 247, 250);
            colors.AsSpan().Fill(background);
            var glyphs = new int[12][];
            for (var i = 0; i < glyphs.Length; i++)
            {
                glyphs[i] = new int[70];
                for (var j = 0; j < 70; j++)
                {
                    glyphs[i][j] = Math.Max(0, Next(8) - 3);
                }
            }

            (int R, int G, int B)[] inks = [(20, 24, 31), (9, 105, 218), (207, 34, 46)];
            for (var y = 0; y < 20; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    colors[(y * width) + x] = (30 + (x * 200 / width), 41, 59);
                }
            }

            for (var line = 0; (line * 14) + 30 < height - 10; line++)
            {
                var ink = inks[line % 3 == 2 ? Next(3) : 0];
                for (var x = 10; x + 8 < width - 10; x += 8)
                {
                    if (Next(6) == 0)
                        continue;

                    var glyph = glyphs[Next(glyphs.Length)];
                    for (var gy = 0; gy < 10; gy++)
                    {
                        for (var gx = 0; gx < 7; gx++)
                        {
                            var coverage = glyph[(gy * 7) + gx];
                            colors[((((line * 14) + 30 + gy) * width) + x + gx)] = (Mix(background.Item1, ink.R, coverage), Mix(background.Item2, ink.G, coverage), Mix(background.Item3, ink.B, coverage));
                        }
                    }
                }
            }
        }

        var data = new byte[layout.GetByteLength(width, height)];
        var offset = 0;
        foreach (var (r, g, b) in colors)
        {
            for (var channel = 0; channel < layout.ChannelCount; channel++)
            {
                var value = channel == layout.AlphaChannel ? 255 : !layout.IsColor ? ((2 * r) + (5 * g) + b) / 8 : channel switch
                {
                    0 => r,
                    1 => g,
                    _ => b,
                };

                if (layout.BytesPerSample == 1)
                {
                    data[offset++] = (byte)value;
                }
                else
                {
                    data[offset++] = (byte)value;
                    data[offset++] = (byte)value;
                }
            }
        }

        return RawPixelBuffer.Create(width, height, layout, data);

        static int Mix(int from, int to, int coverage) => from + ((to - from) * coverage / 4);
    }

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
                        data[offset++] = (byte)(value >> 4);
                    }
                    else
                    {
                        data[offset++] = (byte)value;
                        data[offset++] = (byte)(value >> 8);
                    }
                }
            }
        }

        return RawPixelBuffer.Create(width, height, layout, data);
    }
}
