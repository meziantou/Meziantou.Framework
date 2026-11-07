using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using static Meziantou.Framework.Imaging.InteropTests.HeicDecoderInteropTests;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The output of the HEIC encoder, read by independent implementations. Every file of a matrix of sample formats
/// (4:4:4, 4:2:2, 4:2:0 and monochrome; 8, 10 and 12 bits; lossless and lossy; with and without alpha; still images and
/// image sequences; odd sizes; a spread of efforts and qualities) goes through:
/// <list type="bullet">
/// <item><description>
/// <b>The reference HEVC decoders.</b> <see cref="ReferenceHeif"/> extracts the stream of each item or track, and two of
/// FFmpeg, dec265 and HM must decode it to the same planes (<see cref="HeicDecoderInteropTests"/> has the rule and the
/// conversion of the planes to pixels). These planes are the planes that the decoder of the library gives for the stream,
/// sample for sample: the reconstruction is exactly specified, so any difference is a stream that the encoder got wrong.
/// The pixels of a lossless file are the pixels of its source.
/// </description></item>
/// <item><description>
/// <b>libheif.</b> <c>heif-info</c> reads the brands, the sample format, the color description, the clean aperture, the
/// tracks and their play count. <c>heif-dec</c> decodes the still images and the images of the sequences: its pixels are
/// those of the library exactly for lossless files, for monochrome images and for alpha, within the rounding of a matrix
/// for 4:4:4 samples, and those of the same planes converted by its own rules for subsampled chroma, whose samples it
/// does not place where the stream says they are (the comparisons of <see cref="HeicDecoderInteropTests"/>).
/// </description></item>
/// <item><description>
/// <b>FFmpeg.</b> <c>ffprobe</c> reads the whole file: its streams (the items, then the tracks), their profile, their
/// pixel format, their range and the number of their pictures, all of which it decodes.
/// </description></item>
/// <item><description>
/// <b>Apple ImageIO</b>, on macOS, decodes the still images and the image sequences of 8 and 10 bits per sample.
/// </description></item>
/// </list>
/// What the readers do with the files, beyond what the comparisons accept:
/// <list type="bullet">
/// <item><description>
/// <c>heif-dec -S</c> ignores the clean aperture of a track: the images of a sequence of an odd size with subsampled
/// chroma come out with the even size of the coded pictures, one column or one row larger than the images. It applies the
/// clean aperture of an item. FFmpeg reports the even size too, for items and for tracks.
/// </description></item>
/// <item><description>
/// The pinned <c>dec265</c> command-line tool aborts when it writes a monochrome picture of more than 8 bits that has a
/// conformance window (see <see cref="HevcEncoderInteropTests"/>): the planes of the alpha planes and of the gray images of
/// 10 and 12 bits are then the ones of FFmpeg and HM. libheif, which has the same decoder, decodes these files.
/// </description></item>
/// <item><description>
/// The native decoder of the pinned FFmpeg outputs other pictures, without any error, for the 12-bit lossy streams of the
/// qualities 0 to 2, whose quantization parameter is 50 or 51 (see <see cref="HevcEncoderInteropTests"/>): their planes are
/// the ones of dec265 and HM, which are also the ones of the library and of heif-dec.
/// </description></item>
/// <item><description>
/// <c>heif-info</c> prints <c>unsupported editlist</c> for the edit list of a sequence that is played once (an edit that
/// is not repeated), and no color profile for the color description of a monochrome image, whose code points are all
/// "unspecified". <c>heif-dec -S</c> plays the former once, as intended.
/// </description></item>
/// </list>
/// The theories of this class run concurrently: every case writes files of its own name to the artifacts directory.
/// </summary>
public sealed class HeicEncoderInteropTests
{
    private static readonly (int Width, int Height)[] Sizes = [(64, 48), (75, 41), (33, 21), (96, 64), (7, 5), (65, 47), (130, 67), (48, 40), (17, 9), (40, 56), (31, 63), (1, 1)];

    private static readonly Dictionary<string, StillCase> Stills = CreateStillCases().ToDictionary(item => item.Name, StringComparer.Ordinal);
    private static readonly Dictionary<string, SequenceCase> Sequences = CreateSequenceCases().ToDictionary(item => item.Name, StringComparer.Ordinal);

    public static TheoryData<string> StillCases => [.. Stills.Keys];

    public static TheoryData<string> SequenceCases => [.. Sequences.Keys];

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the source.</param>
    /// <param name="Width">The width of the image.</param>
    /// <param name="Height">The height of the image.</param>
    /// <param name="Encoder">The settings of the encoder.</param>
    private sealed record StillCase(string Name, SourceKind Source, int Width, int Height, HeicEncoder Encoder)
    {
        /// <summary>The orientation of the image, which the file stores as a rotation and a mirroring.</summary>
        public ExifOrientation Orientation { get; init; } = ExifOrientation.TopLeft;
    }

    /// <param name="Name">The name of the case and of its files.</param>
    /// <param name="Source">The layout of the sources.</param>
    /// <param name="Width">The width of the images.</param>
    /// <param name="Height">The height of the images.</param>
    /// <param name="Encoder">The settings of the encoder.</param>
    /// <param name="FrameCount">The number of images.</param>
    /// <param name="TotalPlays">The number of times the sequence is played, or <see langword="null"/> when it repeats forever.</param>
    private sealed record SequenceCase(string Name, SourceKind Source, int Width, int Height, HeicEncoder Encoder, int FrameCount, int? TotalPlays);

    private static IEnumerable<StillCase> CreateStillCases()
    {
        // Every sample format, with and without alpha. The sizes, the efforts and the qualities rotate; the slowest
        // searches get the smallest images
        var index = 0;
        foreach (var lossless in new[] { true, false })
        {
            foreach (var format in lossless ? ["444", "400"] : new[] { "420", "422", "444", "400" })
            {
                foreach (var bitDepth in new[] { 8, 10, 12 })
                {
                    foreach (var transparent in format is "400" ? [false] : new[] { false, true })
                    {
                        var (width, height) = Sizes[index % Sizes.Length];
                        var effort = (index * 7) % 10;
                        if (effort >= 8)
                        {
                            (width, height) = (Math.Min(width, 40), Math.Min(height, 33));
                        }

                        var quality = 25 + ((index * 23) % 75);
                        var encoder = new HeicEncoder
                        {
                            Compression = lossless ? HeicCompression.Lossless : HeicCompression.Lossy,
                            Quality = quality,
                            Effort = effort,
                            BitDepth = bitDepth is 8 ? null : bitDepth,
                            ChromaSubsampling = GetSubsampling(format),
                        };
                        var name = string.Create(CultureInfo.InvariantCulture, $"{(lossless ? "lossless" : "lossy")}-{format}-{bitDepth}{(transparent ? "-alpha" : "")}-{width}x{height}-e{effort}{(lossless ? "" : "-q" + quality.ToString(CultureInfo.InvariantCulture))}");
                        yield return new(name, GetSourceKind(format, bitDepth, transparent), width, height, encoder);
                        index++;
                    }
                }
            }
        }

        // The defaults: a lossless file, and a lossy one that every HEIC reader decodes (8-bit 4:2:0, Main Still Picture)
        yield return new("default-lossless", SourceKind.Rgba8, 96, 64, new HeicEncoder());
        yield return new("default-lossy", SourceKind.Rgb8, 96, 64, new HeicEncoder { Compression = HeicCompression.Lossy });
        yield return new("default-lossy-alpha-odd", SourceKind.Rgba8, 97, 65, new HeicEncoder { Compression = HeicCompression.Lossy });
        yield return new("optimized-lossy-alpha", SourceKind.Rgba8, 97, 65, new HeicEncoder { Compression = HeicCompression.Lossy, OptimizeQuantization = true });

        // 8-bit pixels in samples of more bits, and 16-bit pixels reduced to 12 bits without a bit depth
        yield return new("lossless-444-8-as-10", SourceKind.Rgba8, 33, 21, new HeicEncoder { BitDepth = 10 });
        yield return new("lossless-444-16-reduced", SourceKind.Rgba16, 33, 21, new HeicEncoder { AllowBitDepthReduction = true });
        yield return new("lossy-420-8-as-12", SourceKind.Rgb8, 65, 47, new HeicEncoder { Compression = HeicCompression.Lossy, BitDepth = 12 });

        // The two ends of the qualities at the two ends of the efforts
        foreach (var effort in new[] { 0, 9 })
        {
            foreach (var quality in new[] { 0, 100 })
            {
                yield return new(string.Create(CultureInfo.InvariantCulture, $"lossy-420-8-alpha-41x27-e{effort}-q{quality}"), SourceKind.Rgba8, 41, 27, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality, Effort = effort });
                yield return new(string.Create(CultureInfo.InvariantCulture, $"lossy-444-12-24x24-e{effort}-q{quality}"), SourceKind.Rgb16, 24, 24, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality, Effort = effort, BitDepth = 12, ChromaSubsampling = HeicChromaSubsampling.Ratio444 });
            }
        }

        // Images of several rows of coding tree units, at the default effort
        yield return new("lossless-444-8-alpha-200x136", SourceKind.Rgba8, 200, 136, new HeicEncoder());
        yield return new("lossy-420-8-201x137", SourceKind.Rgb8, 201, 137, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 60 });
        yield return new("lossy-422-10-alpha-200x136", SourceKind.Rgba16, 200, 136, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 80, BitDepth = 10, ChromaSubsampling = HeicChromaSubsampling.Ratio422 });

        // Images large enough for the rows of coding tree blocks to be substreams of the wavefront, with their entry points
        yield return new("wavefront-lossy-420-8-alpha-321x241", SourceKind.Rgba8, 321, 241, new HeicEncoder { Compression = HeicCompression.Lossy, Effort = 2 });
        yield return new("wavefront-lossy-444-10-288x240", SourceKind.Rgb16, 288, 240, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 40, BitDepth = 10, ChromaSubsampling = HeicChromaSubsampling.Ratio444, Effort = 4 });
        yield return new("wavefront-lossless-444-8-320x208", SourceKind.Rgb8, 320, 208, new HeicEncoder { Effort = 1 });
        yield return new("wavefront-lossless-400-12-257x256", SourceKind.Gray16, 257, 256, new HeicEncoder { BitDepth = 12, Effort = 0 });

        // Pictures of level 5 and of level 6, by their width (ITU-T H.265, clause A.4.1), whose coding tree blocks have 32
        // samples at least
        yield return new("lossy-420-8-4226x9-level-5", SourceKind.Rgb8, 4226, 9, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 70, Effort = 2 });
        yield return new("lossless-444-8-alpha-8448x8-level-6", SourceKind.Rgba8, 8448, 8, new HeicEncoder { Effort = 1 });

        // The transformations of an image: heif-dec applies them, with the clean aperture of an image of an odd size
        yield return new("lossless-444-8-rotated", SourceKind.Rgba8, 33, 21, new HeicEncoder()) { Orientation = ExifOrientation.RightTop };
        yield return new("lossless-400-10-mirrored", SourceKind.Gray16, 33, 21, new HeicEncoder { BitDepth = 10 }) { Orientation = ExifOrientation.TopRight };
        yield return new("lossy-444-8-alpha-transposed", SourceKind.Rgba8, 47, 33, new HeicEncoder { Compression = HeicCompression.Lossy, ChromaSubsampling = HeicChromaSubsampling.Ratio444 }) { Orientation = ExifOrientation.LeftTop };
    }

    private static IEnumerable<SequenceCase> CreateSequenceCases()
    {
        var index = 0;
        foreach (var lossless in new[] { true, false })
        {
            foreach (var format in lossless ? ["444", "400"] : new[] { "420", "422", "444", "400" })
            {
                foreach (var bitDepth in new[] { 8, 10, 12 })
                {
                    // The sequences alternate between alpha and none; the gray pixel formats have none
                    var transparent = format is not "400" && (index & 1) is 0;
                    var (width, height) = Sizes[(index + 1) % Sizes.Length];
                    var effort = (index * 3) % 10;
                    if (effort >= 8)
                    {
                        (width, height) = (Math.Min(width, 40), Math.Min(height, 33));
                    }

                    var quality = 30 + ((index * 17) % 70);
                    var keyFrameInterval = index % 4;
                    var encoder = new HeicEncoder
                    {
                        Compression = lossless ? HeicCompression.Lossless : HeicCompression.Lossy,
                        Quality = quality,
                        Effort = effort,
                        BitDepth = bitDepth is 8 ? null : bitDepth,
                        ChromaSubsampling = GetSubsampling(format),
                        KeyFrameInterval = keyFrameInterval,
                    };
                    var name = string.Create(CultureInfo.InvariantCulture, $"sequence-{(lossless ? "lossless" : "lossy")}-{format}-{bitDepth}{(transparent ? "-alpha" : "")}-{width}x{height}-e{effort}-k{keyFrameInterval}{(lossless ? "" : "-q" + quality.ToString(CultureInfo.InvariantCulture))}");
                    yield return new(name, GetSourceKind(format, bitDepth, transparent), width, height, encoder, FrameCount: 4 + (index % 3), TotalPlays: (index % 3) switch { 0 => null, 1 => 1, _ => 3 });
                    index++;
                }
            }
        }

        // The defaults, and the sequences of the profiles that every HEIC reader has (8-bit 4:2:0: Main), with an alpha
        // track and an odd size
        yield return new("sequence-default-lossless", SourceKind.Rgba8, 96, 64, new HeicEncoder(), FrameCount: 4, TotalPlays: null);
        yield return new("sequence-default-lossy", SourceKind.Rgb8, 96, 64, new HeicEncoder { Compression = HeicCompression.Lossy }, FrameCount: 6, TotalPlays: 2);
        yield return new("sequence-default-lossy-alpha-odd", SourceKind.Rgba8, 97, 65, new HeicEncoder { Compression = HeicCompression.Lossy }, FrameCount: 5, TotalPlays: null);
        yield return new("sequence-optimized-lossy-alpha", SourceKind.Rgba8, 65, 33, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 40, Effort = 2, OptimizeQuantization = true }, FrameCount: 3, TotalPlays: 1);
        yield return new("sequence-lossy-420-8-alpha-1x1", SourceKind.Rgba8, 1, 1, new HeicEncoder { Compression = HeicCompression.Lossy }, FrameCount: 3, TotalPlays: 1);

        // Sequences of several rows of coding tree units, whose pictures are predicted from each other
        yield return new("sequence-lossy-420-8-200x136", SourceKind.Rgb8, 200, 136, new HeicEncoder { Compression = HeicCompression.Lossy, Quality = 55 }, FrameCount: 4, TotalPlays: null);
        yield return new("sequence-lossless-444-10-alpha-136x72", SourceKind.Rgba16, 136, 72, new HeicEncoder { BitDepth = 10, Effort = 3 }, FrameCount: 3, TotalPlays: 4);
    }

    private static HeicChromaSubsampling GetSubsampling(string format) => format switch
    {
        "444" => HeicChromaSubsampling.Ratio444,
        "422" => HeicChromaSubsampling.Ratio422,
        _ => HeicChromaSubsampling.Ratio420,
    };

    // The 16-bit sources have four samples per pixel: they are opaque unless the case has alpha
    private static SourceKind GetSourceKind(string format, int bitDepth, bool transparent) => (format, bitDepth, transparent) switch
    {
        ("400", 8, _) => SourceKind.Gray8,
        ("400", _, _) => SourceKind.Gray16,
        (_, 8, false) => SourceKind.Rgb8,
        (_, 8, true) => SourceKind.Rgba8,
        (_, _, false) => SourceKind.Rgb16,
        _ => SourceKind.Rgba16,
    };

    [Theory]
    [MemberData(nameof(StillCases))]
    public async Task StillImagesAreReadByTheReferences(string name)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var item = Stills[name];
        var source = CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name));
        using var image = CreateImage(source);
        image.Metadata.Orientation = item.Orientation;
        var path = ArtifactPath(name + ".heic");
        await image.SaveAsync(path, item.Encoder, XunitCancellationToken);

        var lossless = item.Encoder.Compression is HeicCompression.Lossless;
        var expected = GetExpectedStream(item.Source, item.Encoder, item.Width, item.Height, isSequence: false);
        var comparison = expected.ChromaFormatIdc is 0 || lossless ? HeifDecComparison.Exact : expected.ChromaFormatIdc is 3 ? HeifDecComparison.Matrix : HeifDecComparison.SitingDiffers;

        // The file through the container reader of the harness, heif-info, the reference decoders and heif-dec, and the
        // pixels of the library against all of them
        var still = await AssertStillAsync(heif, path, "encoder-" + name, comparison);
        Assert.Equal((item.Width, item.Height), (still.Width, still.Height));
        Assert.Equal(item.Orientation, still.Orientation);
        Assert.Equal((expected.CodedWidth, expected.CodedHeight, expected.BitDepth, expected.ChromaFormatIdc), (still.Color.Width, still.Color.Height, still.Color.BitDepth, still.Color.ChromaFormatIdc));
        Assert.Equal(expected.HasAlpha, still.Alpha is not null);

        // The video signal description of the streams, as FFmpeg reads it: the range is always written, the matrix is the
        // one of the container, and the chroma samples of 4:2:0 pictures are at the left of their luma samples
        var colorSignal = still.Color.Signal;
        Assert.Equal((true, true, expected.MatrixCoefficients, 0), (colorSignal.HasVideoSignalType, colorSignal.FullRange, colorSignal.HasColorDescription ? colorSignal.MatrixCoefficients : 2, colorSignal.ChromaSampleLocType));
        Assert.Equal(new ReferenceHeif.ColorDescription(2, 2, expected.MatrixCoefficients, FullRange: true), still.Reader.PrimaryItem!.Nclx);
        Assert.Equal([2], colorSignal.SliceTypes);
        if (still.Alpha is { } alphaPlanes)
        {
            Assert.Equal((0, expected.BitDepth, true, true), (alphaPlanes.ChromaFormatIdc, alphaPlanes.BitDepth, alphaPlanes.Signal.HasVideoSignalType, alphaPlanes.Signal.FullRange));
            Assert.Equal(ReferenceHeif.HevcAlphaAuxiliaryType, still.Reader.AlphaItem!.AuxiliaryType);
        }

        // The brands, as libheif reads them: the ones of the profile of the color image
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((expected.ImageBrand, "image/heic"), (info.MajorBrand, info.MimeType));
        Assert.Equal([expected.ImageBrand, "mif1", "miaf"], info.CompatibleBrands);
        Assert.Equal(expected.LibHeifColorSpace, info.ColorSpace);
        Assert.Equal((expected.CodedWidth - item.Width, expected.CodedHeight - item.Height) is (0, 0) ? null : (0, 0, expected.CodedWidth - item.Width, expected.CodedHeight - item.Height), info.Crop);

        // The planes of the reference decoders are the planes of the decoder of the library
        var reader = still.Reader;
        AssertLibraryPlanes(name + " color", reader.PrimaryItem.HevcConfiguration!, [reader.ReadItemData(reader.PrimaryItem)], [still.Color]);
        if (still.Alpha is not null)
        {
            AssertLibraryPlanes(name + " alpha", reader.AlphaItem!.HevcConfiguration!, [reader.ReadItemData(reader.AlphaItem)], [still.Alpha]);
        }

        // A lossless file gives back its source: every sample, hidden colors included, rounded to the bit depth of the file
        var coded = ToCodedPixels(source, expected.BitDepth);
        if (lossless)
        {
            AssertPixels(coded, still.Pixels, ColorBound.Exact, $"{name}: library vs source");
        }
        else
        {
            Assert.Null(MeasurePixels(coded, still.Pixels).FirstAlphaMismatch);
        }

        // heif-dec, whatever the decoders the planes come from: libheif decodes every file. It applies the transformations,
        // which the library reports: its pixels are those of the library once the image is oriented
        var decoded = await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        var report = "";
        if (item.Orientation is not ExifOrientation.TopLeft)
        {
            Assert.NotEqual(HeifDecComparison.SitingDiffers, comparison);
            using var oriented = Image.Load(path);
            oriented.AutoOrient(XunitCancellationToken);
            var measured = AssertPixels(decoded.GetPixels(expected.BitDepth), ToRgba16(ImageSnapshots.CaptureFrame(oriented.Frames[0])), comparison is HeifDecComparison.Exact ? ColorBound.Exact : ColorBound.Matrix(expected.BitDepth, fullRange: true), $"{name}: library vs heif-dec {heif.Version}, oriented");
            report = string.Create(CultureInfo.InvariantCulture, $"library vs heif-dec, oriented: max {measured.Max}");
        }
        else
        {
            Assert.Equal((item.Width, item.Height), (decoded.Width, decoded.Height));
            var heifDec = decoded.GetPixels(expected.BitDepth);
            if (lossless)
            {
                AssertPixels(coded, heifDec, ColorBound.Exact, $"{name}: heif-dec {heif.Version} vs source");
            }
            else
            {
                var (matrix, fullRange) = ResolveColor(reader.PrimaryItem.Nclx, colorSignal);
                report = CompareWithHeifDec(name, comparison, heif, still.Color, still.Alpha, matrix, fullRange, still.Crop, decoded, still.Pixels, isSequence: false);
            }
        }

        // FFmpeg on the whole file: one stream per item, with the profile and the pixel format of its pictures
        var streams = await ProbeAsync(path);
        var expectedStreams = new List<ProbedStream> { new(expected.FFmpegProfile, expected.FFmpegPixelFormat, expected.CodedWidth, expected.CodedHeight, "pc", 1) };
        if (expected.HasAlpha)
        {
            expectedStreams.Add(new("Rext", GetGrayPixelFormat(expected.BitDepth), expected.CodedWidth, expected.CodedHeight, "pc", 1));
        }

        Assert.Equal(expectedStreams, streams);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: {new FileInfo(path).Length} bytes, {info.MajorBrand}, {expected.FFmpegProfile} {expected.FFmpegPixelFormat}, planes of {still.Color.Decoders}{(still.Alpha is null ? "" : " and " + still.Alpha.Decoders)}{(report.Length is 0 ? "" : "; " + report)}"));
    }

    [Theory]
    [MemberData(nameof(SequenceCases))]
    public async Task ImageSequencesAreReadByTheReferences(string name)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var item = Sequences[name];
        var sources = new List<RawPixelBuffer>();
        var durations = new List<FrameDuration>();
        using var image = CreateImage(CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name)));
        for (var i = 0; i < item.FrameCount; i++)
        {
            sources.Add(CreateSource(item.Source, item.Width, item.Height, seed: StableSeed(name), frame: i));
            if (i > 0)
            {
                using var next = CreateImage(sources[i]);
                image.AppendFrame(next.Frames[0]);
            }

            // 1/10, 1/25 and 3/2 of a second: the time scale that holds them exactly is 50
            durations.Add((i % 3) switch { 0 => new FrameDuration(1, 10), 1 => new FrameDuration(1, 25), _ => new FrameDuration(3, 2) });
            image.Frames[i].Metadata.Duration = durations[i];
        }

        image.Animation = new AnimationMetadata { TotalPlays = item.TotalPlays };
        var path = ArtifactPath(name + ".heics");
        await image.SaveAsync(path, item.Encoder, XunitCancellationToken);

        var lossless = item.Encoder.Compression is HeicCompression.Lossless;
        var expected = GetExpectedStream(item.Source, item.Encoder, item.Width, item.Height, isSequence: true);
        var bytes = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(bytes);
        var track = reader.ColorTrack ?? throw new InvalidDataException($"{name}: the file has no track of images.");
        var alphaTrack = reader.AlphaTrack;
        Assert.Equal(expected.HasAlphaTrack, alphaTrack is not null);
        Assert.Equal((expected.CodedWidth, expected.CodedHeight, item.FrameCount), (track.Width, track.Height, track.Samples.Count));
        long[] sampleDurations = [.. durations.Select(duration => duration.Numerator * 50 / duration.Denominator)];
        Assert.Equal(50u, track.Timescale);
        Assert.Equal(sampleDurations, track.Samples.Select(sample => (long)sample.Duration));

        // heif-info: the brands of the profile of the color images, the tracks, the size of their pictures and their play count
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((expected.SequenceBrand, "image/heic-sequence"), (info.MajorBrand, info.MimeType));
        Assert.Equal([expected.ImageBrand, expected.SequenceBrand, "msf1", "iso8", "mif1", "miaf"], info.CompatibleBrands);
        Assert.Equal<long?>(50, info.MovieTimeScale);
        Assert.Equal(expected.HasAlphaTrack ? ["pict", "auxv"] : ["pict"], info.Tracks.Select(candidate => candidate.Handler));
        Assert.Equal<(int, int)?>((expected.CodedWidth, expected.CodedHeight), info.Tracks[0].Resolution);

        // The play count: heif-info reads the edit list that is repeated, and does not read the one that is not, which is
        // how a sequence says that it is played once
        Assert.All(info.Tracks, candidate => Assert.Equal((item.TotalPlays is 1 ? null : item.TotalPlays, item.TotalPlays is 1, "hvc1"), (candidate.Repetitions, candidate.HasUnsupportedEditList, candidate.SampleEntryType)));

        // The first image is also a still image, which libheif describes with its clean aperture
        Assert.Equal((item.Width, item.Height, expected.BitDepth, expected.LibHeifColorSpace, expected.HasAlphaTrack), (info.Width, info.Height, info.BitDepth, info.ColorSpace, info.HasAlpha));

        // The reference decoders: the planes of every picture, which are the planes of the decoder of the library
        var color = await DecodeTrackAsync("encoder-" + name + ".color", reader, track);
        var alpha = alphaTrack is null ? null : await DecodeTrackAsync("encoder-" + name + ".alpha", reader, alphaTrack);
        Assert.Equal((expected.BitDepth, expected.ChromaFormatIdc), (color[0].BitDepth, color[0].ChromaFormatIdc));
        AssertLibraryPlanes(name + " color", track.HevcConfiguration!, [.. track.Samples.Select(reader.ReadSample)], color);
        if (alpha is not null)
        {
            AssertLibraryPlanes(name + " alpha", alphaTrack!.HevcConfiguration!, [.. alphaTrack.Samples.Select(reader.ReadSample)], alpha);
        }

        // The key frames are intra pictures (slice type 2), and the other pictures refer to one picture (slice type 1): the
        // sync samples of the tracks are the key frames
        var interval = item.Encoder.KeyFrameInterval;
        int[] sliceTypes = [.. Enumerable.Range(0, item.FrameCount).Select(i => i is 0 || (interval > 0 && i % interval is 0) ? 2 : 1)];
        uint[]? syncSamples = interval is 1 ? null : [.. Enumerable.Range(0, item.FrameCount).Where(i => sliceTypes[i] is 2).Select(i => (uint)(i + 1))];
        Assert.Equal(sliceTypes, color[0].Signal.SliceTypes);
        Assert.Equal(syncSamples, track.SyncSamples);
        if (alpha is not null)
        {
            Assert.Equal(sliceTypes, alpha[0].Signal.SliceTypes);
            Assert.Equal(syncSamples, alphaTrack!.SyncSamples);
        }

        // The images are the clean aperture of the pictures
        var pictures = (X: 0, Y: 0, track.Width, track.Height);
        var crop = track.CleanAperture is { } aperture
            ? aperture.GetRectangle(track.Width, track.Height) ?? throw new InvalidDataException($"{name}: the clean aperture does not select whole samples.")
            : pictures;
        Assert.Equal((0, 0, item.Width, item.Height), crop);
        Assert.Equal(crop != pictures, track.CleanAperture is not null);
        Assert.Equal(track.CleanAperture, alphaTrack?.CleanAperture ?? track.CleanAperture);
        Assert.Equal<(double, double)>((item.Width, item.Height), (track.PresentationWidth, track.PresentationHeight));

        // The library: the planes converted by the oracle, the durations and the play count of the source
        var (matrix, fullRange) = ResolveColor(track.Nclx, color[0].Signal);
        Assert.Equal((expected.MatrixCoefficients, true), (matrix, fullRange));
        using var loaded = Image.Load(path);
        Assert.Equal(ExpectedPixelFormat(expected.BitDepth, expected.ChromaFormatIdc is 0, alpha is not null), loaded.PixelFormat);
        Assert.Equal(new Size(item.Width, item.Height), loaded.Size);
        Assert.Equal(item.FrameCount, loaded.Frames.Count);
        Assert.Equal(durations, loaded.Frames.Select(frame => frame.Metadata.Duration));
        Assert.Equal(item.TotalPlays, loaded.Animation?.TotalPlays);
        var oracleBound = ColorBound.Oracle(color[0], alpha?[0], matrix, fullRange);
        var frames = new List<RawPixelBuffer>();
        for (var i = 0; i < item.FrameCount; i++)
        {
            var frameName = string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}");
            frames.Add(ToRgba16(ImageSnapshots.CaptureFrame(loaded.Frames[i])));
            AssertPixels(ConvertToPixels(color[i], alpha?[i], matrix, fullRange, crop, ChromaModel.Declared), frames[i], oracleBound, $"{frameName}: library vs the planes of {color[i].Decoders} converted by the oracle");
            var coded = ToCodedPixels(sources[i], expected.BitDepth);
            if (lossless)
            {
                AssertPixels(coded, frames[i], ColorBound.Exact, $"{frameName}: library vs source");
            }
            else
            {
                Assert.Null(MeasurePixels(coded, frames[i]).FirstAlphaMismatch);
            }
        }

        // heif-dec -S: every image once, with the duration of its sample. It ignores the clean aperture of a track: its
        // images have the size of the coded pictures, and are compared with the whole pictures, whose part outside the
        // image the library does not show (the oracle gives it)
        var comparison = Classify(color[0], matrix, fullRange, pictures, isSequence: true);
        Assert.Equal(expected.ChromaFormatIdc is 0 || lossless ? HeifDecComparison.Exact : expected.ChromaFormatIdc is 3 ? HeifDecComparison.Matrix : HeifDecComparison.SitingDiffers, comparison);
        var references = await heif.DecodeSequenceAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal(sampleDurations, references.Select(reference => reference.Duration));
        var report = "";
        for (var i = 0; i < item.FrameCount; i++)
        {
            var frameName = string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}");
            Assert.Equal((track.Width, track.Height), (references[i].Image.Width, references[i].Image.Height), message: $"{frameName}: the size of the image of heif-dec.");
            var whole = Overlay(ConvertToPixels(color[i], alpha?[i], matrix, fullRange, pictures, ChromaModel.Declared), frames[i]);
            report = CompareWithHeifDec(frameName, comparison, heif, color[i], alpha?[i], matrix, fullRange, pictures, references[i].Image, whole, isSequence: true);
        }

        // heif-dec plays a sequence as many times as its edit list says: once without the repeat flag
        if (item.TotalPlays is { } plays)
        {
            var played = await heif.DecodeSequenceAsync(path, bilinearChroma: true, followEditList: true, XunitCancellationToken);
            Assert.Equal(Enumerable.Repeat(sampleDurations, plays).SelectMany(repetition => repetition), played.Select(reference => reference.Duration));
        }

        // FFmpeg on the whole file: the items of the first image, which are the first samples of the tracks and have their
        // configuration, then the tracks, whose pictures it all decodes
        var streams = await ProbeAsync(path);
        var gray = GetGrayPixelFormat(expected.BitDepth);
        List<ProbedStream> expectedStreams = [new(expected.FFmpegProfile, expected.FFmpegPixelFormat, expected.CodedWidth, expected.CodedHeight, "pc", 1)];
        if (expected.HasAlphaTrack)
        {
            expectedStreams.Add(new("Rext", gray, expected.CodedWidth, expected.CodedHeight, "pc", 1));
        }

        expectedStreams.Add(new(expected.FFmpegProfile, expected.FFmpegPixelFormat, expected.CodedWidth, expected.CodedHeight, "pc", item.FrameCount));
        if (expected.HasAlphaTrack)
        {
            expectedStreams.Add(new("Rext", gray, expected.CodedWidth, expected.CodedHeight, "pc", item.FrameCount));
        }

        Assert.Equal(expectedStreams, streams);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: {bytes.Length} bytes, {info.MajorBrand}, {expected.FFmpegProfile} {expected.FFmpegPixelFormat}, {item.FrameCount} pictures (slice types {string.Join(' ', sliceTypes)}), planes of {color[0].Decoders}{(alpha is null ? "" : " and " + alpha[0].Decoders)}; last frame: {report}"));
    }

    [Theory]
    [InlineData(ExifOrientation.TopLeft, null, null)]
    [InlineData(ExifOrientation.TopRight, null, "horizontal")]
    [InlineData(ExifOrientation.BottomRight, 180, null)]
    [InlineData(ExifOrientation.BottomLeft, null, "vertical")]
    [InlineData(ExifOrientation.LeftTop, 90, "vertical")]
    [InlineData(ExifOrientation.RightTop, 270, null)]
    [InlineData(ExifOrientation.RightBottom, 270, "vertical")]
    [InlineData(ExifOrientation.LeftBottom, 90, null)]
    public async Task MetadataAndTransformationsAreReportedByHeifInfo(ExifOrientation orientation, int? rotation, string? mirror)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "metadata-" + orientation;
        var source = CreateSource(SourceKind.Rgba8, 31, 17, seed: StableSeed(name));
        using var image = CreateImage(source);
        var xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"/></x:xmpmeta>"u8.ToArray();

        // A TIFF structure of one field, the orientation, which the library keeps equal to the orientation of the image
        byte[] exif = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        image.Metadata.Orientation = orientation;
        image.Metadata.IccProfile = IccProfile.Srgb;
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(exif));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Cicp = new CicpColorDescription(1, 13, 1, fullRange: false);
        var path = ArtifactPath(name + ".heic");
        await image.SaveAsync(path, new HeicEncoder { Compression = HeicCompression.Lossy, ChromaSubsampling = HeicChromaSubsampling.Ratio422, Quality = 90 }, XunitCancellationToken);
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);

        // The color description of the image, with its matrix and its range: the lossy coding keeps them
        Assert.Equal(("YCbCr, 4:2:2", 8, true), (info.ColorSpace, info.BitDepth, info.HasAlpha));
        Assert.Equal<(int?, int?, int?, bool?)>((1, 13, 1, false), (info.ColorPrimaries, info.TransferCharacteristics, info.MatrixCoefficients, info.FullRange));

        // heif-info names the ICC profile, and prints the code points of the color description under it
        Assert.Equal(["prof"], info.ColorProfiles);

        // The item of the EXIF block starts with the four bytes of the offset of the TIFF header
        Assert.Equal<(int?, int?)>((exif.Length + 4, xmp.Length), (info.ExifLength, info.XmpLength));

        // irot is an angle anti-clockwise; imir exchanges the left and the right ("horizontal") or the top and the bottom.
        // The image has an odd width: the clean aperture precedes them
        Assert.Equal((rotation, mirror), (info.RotationAngle, info.Mirror));
        Assert.Equal<(int, int, int, int)?>((0, 0, 1, 0), info.Crop);
        var transposed = orientation is ExifOrientation.LeftTop or ExifOrientation.RightTop or ExifOrientation.RightBottom or ExifOrientation.LeftBottom;
        Assert.Equal(transposed ? (17, 31) : (31, 17), (info.Width, info.Height));

        // The bitstream says what the container says, in limited range
        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        var color = await DecodeItemAsync("encoder-" + name + ".color", reader, reader.PrimaryItem!);
        Assert.Equal((true, false, true, 1, 13, 1), (color.Signal.HasVideoSignalType, color.Signal.FullRange, color.Signal.HasColorDescription, color.Signal.ColorPrimaries, color.Signal.TransferCharacteristics, color.Signal.MatrixCoefficients));
        var alpha = await DecodeItemAsync("encoder-" + name + ".alpha", reader, reader.AlphaItem!);
        Assert.Equal((true, true), (alpha.Signal.HasVideoSignalType, alpha.Signal.FullRange));

        // heif-dec applies the transformations, and reads the alpha plane in full range next to a color image in limited
        // range: the pixels of the library, once oriented, have its alpha plane
        var decoded = await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        using var loaded = Image.Load(path);
        Assert.Equal(orientation, loaded.Metadata.Orientation);
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), loaded.Metadata.IccProfile?.Data.ToArray());
        Assert.Equal(exif, loaded.Metadata.ExifProfile?.Data.ToArray());
        Assert.Equal(xmp, loaded.Metadata.XmpProfile?.Data.ToArray());
        loaded.AutoOrient(XunitCancellationToken);
        var oriented = ToRgba16(ImageSnapshots.CaptureFrame(loaded.Frames[0]));
        Assert.Equal((oriented.Width, oriented.Height), (decoded.Width, decoded.Height));
        Assert.Null(MeasurePixels(decoded.GetPixels(8), oriented).FirstAlphaMismatch);

        // FFmpeg reads the limited range: the pixel format of 8-bit 4:2:2 samples, and the range of the stream
        var streams = await ProbeAsync(path);
        Assert.Equal([new("Rext", "yuv422p", 32, 17, "tv", 1), new("Rext", "gray", 32, 17, "pc", 1)], streams);
    }

    /// <summary>
    /// Apple ImageIO opens and decodes the still images that the encoder writes with 8 and 10 bits per sample, in every
    /// chroma format, lossless or not, with and without alpha, of even and odd sizes. The helper
    /// (<c>imageio_frames.swift</c>) returns the pixels that ImageIO gives in a layout it knows (8-bit samples, or 16-bit
    /// samples with alpha); ImageIO gives the others (the deeper samples of an image without alpha) in a layout that the
    /// helper refuses, once it has decoded them. The pixels are those of the library for the lossless files and for the alpha
    /// planes, within three 8-bit steps for 4:4:4 samples (the bound of the files of Apple in
    /// <see cref="HeicDecoderInteropTests"/>), and are only measured for subsampled chroma, which ImageIO does not upsample
    /// as the stream places it. The 16-bit samples that ImageIO gives to a 10-bit image are not scaled as the library scales
    /// them: both are compared as samples of the bit depth of the image.
    /// <para>
    /// 12-bit images are not part of what the test requires: what ImageIO does with them is written to the test output. On
    /// macOS 27, it decodes the 12-bit monochrome and 4:2:0 images, and fails on the 12-bit 4:2:2 and 4:4:4 ones
    /// (<c>CGImageSourceCreateImageAtIndex</c> returns no image), lossless or not.
    /// </para>
    /// </summary>
    /// <param name="label">The name of the case.</param>
    /// <param name="kind">The layout of the source.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="compression">The compression.</param>
    /// <param name="subsampling">The chroma subsampling of lossy output.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="expectation">
    /// <c>exact</c> (the pixels of the library), <c>matrix</c> (within three 8-bit steps), <c>measured</c> (alpha is exact,
    /// the colors are only measured), <c>decoded</c> (ImageIO decodes the image to a layout that the helper does not
    /// convert) or <c>informative</c> (nothing is required: the outcome is written to the test output).
    /// </param>
    [Theory]
    [InlineData("lossless-default", SourceKind.Rgb8, 64, 48, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 8, "exact")]
    [InlineData("lossless-alpha", SourceKind.Rgba8, 64, 48, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 8, "exact")]
    [InlineData("lossless-odd", SourceKind.Rgb8, 75, 41, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 8, "exact")]
    [InlineData("lossless-gray", SourceKind.Gray8, 75, 41, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 8, "exact")]
    [InlineData("lossless-alpha-10", SourceKind.Rgba16, 75, 41, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 10, "exact")]
    [InlineData("lossless-10", SourceKind.Rgb16, 64, 48, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 10, "decoded")]
    [InlineData("lossless-gray-10", SourceKind.Gray16, 75, 41, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 10, "decoded")]
    [InlineData("lossless-12", SourceKind.Rgb16, 64, 48, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 12, "informative")]
    [InlineData("lossless-gray-12", SourceKind.Gray16, 64, 48, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 12, "informative")]
    [InlineData("lossy-420", SourceKind.Rgb8, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 8, "measured")]
    [InlineData("lossy-420-odd", SourceKind.Rgb8, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 8, "measured")]
    [InlineData("lossy-420-alpha-odd", SourceKind.Rgba8, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 8, "measured")]
    [InlineData("lossy-422", SourceKind.Rgb8, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio422, 8, "measured")]
    [InlineData("lossy-422-odd", SourceKind.Rgb8, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio422, 8, "measured")]
    [InlineData("lossy-444", SourceKind.Rgb8, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, 8, "matrix")]
    [InlineData("lossy-444-alpha-odd", SourceKind.Rgba8, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, 8, "matrix")]
    [InlineData("lossy-420-10", SourceKind.Rgb16, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 10, "decoded")]
    [InlineData("lossy-420-10-alpha-odd", SourceKind.Rgba16, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 10, "measured")]
    [InlineData("lossy-422-10-odd", SourceKind.Rgb16, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio422, 10, "decoded")]
    [InlineData("lossy-444-10-alpha", SourceKind.Rgba16, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, 10, "matrix")]
    [InlineData("lossy-420-12", SourceKind.Rgb16, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 12, "informative")]
    [InlineData("lossy-422-12", SourceKind.Rgb16, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio422, 12, "informative")]
    [InlineData("lossy-444-12-alpha", SourceKind.Rgba16, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, 12, "informative")]
    [InlineData("lossy-gray", SourceKind.Gray8, 64, 48, HeicCompression.Lossy, HeicChromaSubsampling.Auto, 8, "matrix")]
    [InlineData("lossy-420-wavefront", SourceKind.Rgb8, 320, 240, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 8, "measured")]
    [InlineData("lossless-alpha-wavefront", SourceKind.Rgba8, 320, 240, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 8, "exact")]
    public async Task StillImagesDecodeWithAppleImageIO(string label, SourceKind kind, int width, int height, HeicCompression compression, HeicChromaSubsampling subsampling, int bitDepth, string expectation)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        var name = "imageio-" + label;
        var source = CreateSource(kind, width, height, seed: StableSeed(name));
        using var image = CreateImage(source);
        var path = ArtifactPath(name + ".heic");
        await image.SaveAsync(path, new HeicEncoder { Compression = compression, ChromaSubsampling = subsampling, Quality = 95, BitDepth = bitDepth is 8 ? null : bitDepth }, XunitCancellationToken);
        using var loaded = Image.Load(path);
        var library = ToRgba16(ImageSnapshots.CaptureFrame(loaded.Frames[0]));

        // ImageIO returns an opaque image without alpha channel, which the helper only accepts with an option: the one of
        // the gray images, or the one of the images whose alpha samples are all at the ends of the range
        var hasAlpha = kind is SourceKind.Rgba8 or SourceKind.Rgba16;
        string[] arguments = hasAlpha ? [script, path] : [script, kind is SourceKind.Gray8 or SourceKind.Gray16 ? "--opaque" : "--binary-alpha", path];
        var result = await ProcessRunner.RunAsync(swift, arguments, XunitCancellationToken);
        var lastLine = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        if (expectation is "informative")
        {
            WriteOutput($"{name}: Apple ImageIO exits with the code {result.ExitCode} ({lastLine})");
            return;
        }

        if (expectation is "decoded")
        {
            // Exit code 4: the image is opened and decoded, to samples of more than 8 bits without alpha
            Assert.Equal((4, true), (result.ExitCode, lastLine.StartsWith("unsupported decoded layout: bits 1", StringComparison.Ordinal)), message: $"{name}: ImageIO exits with the code {result.ExitCode}: {result.StandardError}");
            WriteOutput($"{name}: Apple ImageIO decodes the image ({lastLine})");
            return;
        }

        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {name} (exit code {result.ExitCode}): {result.StandardError}");

        // "frames <count> <width> <height> <bits>" is the last line of the error output: the system frameworks can write
        // diagnostics before it
        var header = lastLine.Split(' ');
        var bits = bitDepth is 8 ? 8 : 16;
        Assert.Equal(["frames", "1", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), bits.ToString(CultureInfo.InvariantCulture)], header);
        var apple = ToRgba16(RawPixelBuffer.Create(width, height, bits is 8 ? RawPixelLayout.Rgba8 : RawPixelLayout.Rgba16Le, result.StandardOutput.Span));
        if (bitDepth > 8)
        {
            // The nearest sample of the bit depth of the image, on both sides
            (apple, library) = (ToCodedPixels(apple, bitDepth), ToCodedPixels(library, bitDepth));
        }

        var measured = MeasurePixels(apple, library);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: library vs Apple ImageIO max {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2}{(measured.FirstAlphaMismatch is null ? "" : ", alpha differs")}"));
        switch (expectation)
        {
            case "exact":
                AssertPixels(apple, library, ColorBound.Exact, $"{name}: library vs Apple ImageIO");
                break;

            case "matrix":
                AssertPixels(apple, library, 3 * 257, $"{name}: library vs Apple ImageIO");
                break;

            default:
                Assert.Null(measured.FirstAlphaMismatch);
                break;
        }
    }

    /// <summary>
    /// The grids of <see cref="HeicEncoder.GridTileSize"/>: lossless and lossy, with and without alpha, with tiles that extend
    /// past the image, tiles whose size is not a whole number of coding blocks, and the 512x512 tiles of the photos of
    /// cameras. Each case gives the image, the source, the encoder and what the pixels of a reader must be:
    /// <c>exact</c> (the pixels of the library), <c>matrix</c> (within the rounding of a matrix) or <c>measured</c> (alpha
    /// is exact, the colors of subsampled chroma, which the readers do not place as the stream says, are only measured).
    /// </summary>
    public static TheoryData<string, SourceKind, int, int, int, int, HeicCompression, HeicChromaSubsampling, string> GridCases() => new()
    {
        { "grid-lossless-alpha", SourceKind.Rgba8, 150, 100, 64, 64, HeicCompression.Lossless, HeicChromaSubsampling.Auto, "exact" },
        { "grid-lossless-odd", SourceKind.Rgb8, 65, 130, 64, 65, HeicCompression.Lossless, HeicChromaSubsampling.Auto, "exact" },
        { "grid-lossy-420-camera", SourceKind.Rgb8, 1100, 700, 512, 512, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, "measured" },
        { "grid-lossy-420-alpha", SourceKind.Rgba8, 130, 70, 66, 66, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, "measured" },
        { "grid-lossy-444-odd", SourceKind.Rgb8, 131, 71, 65, 65, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, "matrix" },
    };

    /// <summary>libheif assembles the tiles of the grids, of the color image and of the alpha plane, into the image.</summary>
    [Theory]
    [MemberData(nameof(GridCases))]
    public async Task GridsAreAssembledByHeifDec(string label, SourceKind kind, int width, int height, int tileWidth, int tileHeight, HeicCompression compression, HeicChromaSubsampling subsampling, string expectation)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var (path, library) = await SaveGridAsync(label, kind, width, height, tileWidth, tileHeight, compression, subsampling);
        var info = await heif.GetInfoAsync(path, XunitCancellationToken);
        Assert.Equal((width, height), (info.Width, info.Height));
        var decoded = await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken);
        Assert.Equal((width, height), (decoded.Width, decoded.Height));
        var pixels = decoded.GetPixels(8);
        var measured = MeasurePixels(pixels, library);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{label}: library vs heif-dec {heif.Version} max {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2}"));
        switch (expectation)
        {
            case "exact":
                AssertPixels(pixels, library, ColorBound.Exact, $"{label}: library vs heif-dec {heif.Version}");
                break;

            case "matrix":
                AssertPixels(pixels, library, ColorBound.Matrix(8, fullRange: true), $"{label}: library vs heif-dec {heif.Version}");
                break;

            default:
                Assert.Null(measured.FirstAlphaMismatch);
                break;
        }
    }

    /// <summary>
    /// Apple ImageIO, which reads the grids of the photos of Apple cameras, assembles the tiles of the grids of the encoder,
    /// of the color image and of the alpha plane, into the image.
    /// </summary>
    [Theory]
    [MemberData(nameof(GridCases))]
    public async Task GridsDecodeWithAppleImageIO(string label, SourceKind kind, int width, int height, int tileWidth, int tileHeight, HeicCompression compression, HeicChromaSubsampling subsampling, string expectation)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        var (path, library) = await SaveGridAsync("imageio-" + label, kind, width, height, tileWidth, tileHeight, compression, subsampling);
        string[] arguments = kind is SourceKind.Rgba8 ? [script, path] : [script, "--binary-alpha", path];
        var result = await ProcessRunner.RunAsync(swift, arguments, XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {label} (exit code {result.ExitCode}): {result.StandardError}");

        var lastLine = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        Assert.Equal(["frames", "1", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), "8"], lastLine.Split(' '));
        var apple = ToRgba16(RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba8, result.StandardOutput.Span));
        var measured = MeasurePixels(apple, library);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{label}: library vs Apple ImageIO max {measured.Max} ({measured.Max / 257.0:F3}/255), mean {measured.Mean:F2}"));
        switch (expectation)
        {
            case "exact":
                AssertPixels(apple, library, ColorBound.Exact, $"{label}: library vs Apple ImageIO");
                break;

            case "matrix":
                AssertPixels(apple, library, 3 * 257, $"{label}: library vs Apple ImageIO");
                break;

            default:
                Assert.Null(measured.FirstAlphaMismatch);
                break;
        }
    }

    // Saves a grid, checks its structure, and gives the pixels that the library decodes, which are its source when it is lossless
    private static async Task<(FullPath Path, RawPixelBuffer Pixels)> SaveGridAsync(string name, SourceKind kind, int width, int height, int tileWidth, int tileHeight, HeicCompression compression, HeicChromaSubsampling subsampling)
    {
        var source = CreateSource(kind, width, height, seed: StableSeed(name));
        using var image = CreateImage(source);
        var path = ArtifactPath(name + ".heic");
        await image.SaveAsync(path, new HeicEncoder { Compression = compression, ChromaSubsampling = subsampling, Quality = 95, Effort = 2, GridTileSize = new Size(tileWidth, tileHeight) }, XunitCancellationToken);
        var reader = ReferenceHeif.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal("grid", reader.PrimaryItem!.Type);
        Assert.Equal(kind is SourceKind.Rgba8, reader.AlphaItem?.Type is "grid");
        Assert.All(reader.GetGridTiles(reader.PrimaryItem), tile => Assert.Equal((tileWidth, tileHeight), tile.Size));

        using var loaded = Image.Load(path);
        var library = ToRgba16(ImageSnapshots.CaptureFrame(loaded.Frames[0]));
        if (compression is HeicCompression.Lossless)
            AssertPixels(ToRgba16(source), library, ColorBound.Exact, $"{name}: library vs source");

        return (path, library);
    }

    /// <summary>
    /// Apple ImageIO reads the image sequences: every image, in order. The images that are predicted from the image before
    /// them are decoded like the key frames. The pixels are compared as those of the still images are.
    /// </summary>
    [Theory]
    [InlineData("lossless", SourceKind.Rgba8, 72, 40, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 0, "exact")]
    [InlineData("lossless-key-frames", SourceKind.Rgb8, 72, 40, HeicCompression.Lossless, HeicChromaSubsampling.Auto, 2, "exact")]
    [InlineData("lossy-444", SourceKind.Rgba8, 72, 40, HeicCompression.Lossy, HeicChromaSubsampling.Ratio444, 0, "matrix")]
    [InlineData("lossy-420", SourceKind.Rgb8, 72, 40, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 0, "measured")]
    [InlineData("lossy-420-alpha-odd", SourceKind.Rgba8, 75, 41, HeicCompression.Lossy, HeicChromaSubsampling.Ratio420, 3, "measured")]
    public async Task ImageSequencesDecodeWithAppleImageIO(string label, SourceKind kind, int width, int height, HeicCompression compression, HeicChromaSubsampling subsampling, int keyFrameInterval, string expectation)
    {
        // Apple ImageIO exists only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        const int FrameCount = 5;
        var (swift, script) = RequiredTools.AppleImageIO();
        var name = "imageio-sequence-" + label;
        using var image = CreateImage(CreateSource(kind, width, height, seed: StableSeed(name)));
        for (var i = 1; i < FrameCount; i++)
        {
            using var next = CreateImage(CreateSource(kind, width, height, seed: StableSeed(name), frame: i));
            image.AppendFrame(next.Frames[0]);
        }

        for (var i = 0; i < FrameCount; i++)
        {
            image.Frames[i].Metadata.Duration = new FrameDuration(1, 10);
        }

        var path = ArtifactPath(name + ".heics");
        await image.SaveAsync(path, new HeicEncoder { Compression = compression, ChromaSubsampling = subsampling, Quality = 95, KeyFrameInterval = keyFrameInterval }, XunitCancellationToken);
        using var loaded = Image.Load(path);
        string[] arguments = kind is SourceKind.Rgba8 ? [script, path] : [script, "--binary-alpha", path];
        var result = await ProcessRunner.RunAsync(swift, arguments, XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {name} (exit code {result.ExitCode}): {result.StandardError}");

        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", FrameCount.ToString(CultureInfo.InvariantCulture), width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), "8"], header);
        var frameLength = width * height * 4;
        Assert.Equal(FrameCount * frameLength, result.StandardOutput.Length);
        var maximum = 0;
        for (var i = 0; i < FrameCount; i++)
        {
            var frameName = string.Create(CultureInfo.InvariantCulture, $"{name} frame {i}: library vs Apple ImageIO");
            var apple = ToRgba16(RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba8, result.StandardOutput.Span.Slice(i * frameLength, frameLength)));
            var library = ToRgba16(ImageSnapshots.CaptureFrame(loaded.Frames[i]));
            var measured = MeasurePixels(apple, library);
            maximum = Math.Max(maximum, measured.Max);
            switch (expectation)
            {
                case "exact":
                    AssertPixels(apple, library, ColorBound.Exact, frameName);
                    break;

                case "matrix":
                    AssertPixels(apple, library, 3 * 257, frameName);
                    break;

                default:
                    Assert.Null(measured.FirstAlphaMismatch);
                    break;
            }
        }

        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: {FrameCount} frames, library vs Apple ImageIO max {maximum} ({maximum / 257.0:F3}/255)"));
    }

    /// <summary>What a stream of a file must be, for the settings of its encoder.</summary>
    /// <param name="ChromaFormatIdc"><c>chroma_format_idc</c> of the color pictures.</param>
    /// <param name="BitDepth">The bit depth of the samples.</param>
    /// <param name="CodedWidth">The width of the coded pictures: the width of the image, or one more when its chroma is subsampled and its width is odd.</param>
    /// <param name="CodedHeight">The height of the coded pictures.</param>
    /// <param name="MatrixCoefficients">The matrix coefficients: the identity for lossless color pictures, BT.601 for lossy ones, unspecified for monochrome pictures.</param>
    /// <param name="HasAlpha">Whether a still image has an alpha plane: its source has a pixel that is not opaque.</param>
    /// <param name="HasAlphaTrack">Whether a sequence has an alpha track: its pixel format has alpha.</param>
    /// <param name="ImageBrand">The brand of a still image: <c>heic</c> for the Main and Main Still Picture profiles, otherwise <c>heix</c>.</param>
    /// <param name="SequenceBrand">The brand of an image sequence: <c>hevc</c> or <c>hevx</c>.</param>
    /// <param name="LibHeifColorSpace">The color space that heif-info prints.</param>
    /// <param name="FFmpegProfile">The profile that ffprobe prints.</param>
    /// <param name="FFmpegPixelFormat">The pixel format that ffprobe prints.</param>
    private sealed record ExpectedStream(int ChromaFormatIdc, int BitDepth, int CodedWidth, int CodedHeight, int MatrixCoefficients, bool HasAlpha, bool HasAlphaTrack, string ImageBrand, string SequenceBrand, string LibHeifColorSpace, string FFmpegProfile, string FFmpegPixelFormat);

    private static ExpectedStream GetExpectedStream(SourceKind kind, HeicEncoder encoder, int width, int height, bool isSequence)
    {
        var lossless = encoder.Compression is HeicCompression.Lossless;
        var isGray = kind is SourceKind.Gray8 or SourceKind.Gray16;
        var isDeep = kind is SourceKind.Rgb16 or SourceKind.Rgba16 or SourceKind.Gray16;
        var bitDepth = encoder.BitDepth ?? (isDeep ? 12 : 8);
        var chromaFormatIdc = isGray ? 0 : lossless || encoder.ChromaSubsampling is HeicChromaSubsampling.Ratio444 ? 3 : encoder.ChromaSubsampling is HeicChromaSubsampling.Ratio422 ? 2 : 1;
        var codedWidth = chromaFormatIdc is 1 or 2 ? (width + 1) & ~1 : width;
        var codedHeight = chromaFormatIdc is 1 ? (height + 1) & ~1 : height;
        var suffix = bitDepth is 8 ? "" : string.Create(CultureInfo.InvariantCulture, $"{bitDepth}le");

        // ISO/IEC 23008-12, annex B.4: the Main and Main Still Picture profiles, which are those of 8-bit 4:2:0 pictures
        var isMain = chromaFormatIdc is 1 && bitDepth is 8;
        return new ExpectedStream(
            chromaFormatIdc,
            bitDepth,
            codedWidth,
            codedHeight,
            MatrixCoefficients: isGray ? 2 : lossless ? 0 : 6,
            HasAlpha: kind is SourceKind.Rgba8 or SourceKind.Rgba16,
            HasAlphaTrack: kind is SourceKind.Rgba8 or SourceKind.Rgba16 or SourceKind.Rgb16,
            ImageBrand: isMain ? "heic" : "heix",
            SequenceBrand: isMain ? "hevc" : "hevx",
            LibHeifColorSpace: chromaFormatIdc switch
            {
                0 => "monochrome",
                1 => "YCbCr, 4:2:0",
                2 => "YCbCr, 4:2:2",
                _ => lossless ? "RGB" : "YCbCr, 4:4:4",
            },

            // FFmpeg names the profiles of the first version of the specification, and "Rext" for the format range
            // extensions; it has no name of its own for Main 10 Still Picture
            FFmpegProfile: (chromaFormatIdc, bitDepth) switch
            {
                (1, 8) => isSequence ? "Main" : "Main Still Picture",
                (1, 10) => "Main 10",
                _ => "Rext",
            },

            // The planes of the identity matrix are G, B and R; the full range has a pixel format of its own for 8-bit
            // 4:2:0 samples only
            FFmpegPixelFormat: chromaFormatIdc switch
            {
                0 => GetGrayPixelFormat(bitDepth),
                1 => bitDepth is 8 ? "yuvj420p" : "yuv420p" + suffix,
                2 => "yuv422p" + suffix,
                _ => (lossless ? "gbrp" : "yuv444p") + suffix,
            });
    }

    private static string GetGrayPixelFormat(int bitDepth) => bitDepth is 8 ? "gray" : string.Create(CultureInfo.InvariantCulture, $"gray{bitDepth}le");

    /// <summary>A video stream of a file, as <c>ffprobe</c> describes it once it has decoded its pictures.</summary>
    /// <param name="Profile">The profile of the stream.</param>
    /// <param name="PixelFormat">The pixel format of the decoded pictures.</param>
    /// <param name="Width">The width of the pictures: their conformance window.</param>
    /// <param name="Height">The height of the pictures.</param>
    /// <param name="ColorRange"><c>pc</c> for the full range, <c>tv</c> for the limited range.</param>
    /// <param name="FrameCount">The number of pictures that FFmpeg decoded.</param>
    private sealed record ProbedStream(string Profile, string PixelFormat, int Width, int Height, string ColorRange, int FrameCount);

    /// <summary>Reads every stream of a file with <c>ffprobe</c>, which decodes their pictures to count them.</summary>
    private static async Task<List<ProbedStream>> ProbeAsync(FullPath path)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var result = await ProcessRunner.RunAsync(ffmpeg.ProbeExecutablePath, ["-hide_banner", "-v", "error", "-count_frames", "-show_entries", "stream=codec_name,profile,pix_fmt,width,height,color_range,nb_read_frames", "-of", "json", path], XunitCancellationToken);
        if (result.ExitCode != 0 || result.StandardError.Length > 0)
            Assert.Fail($"ffprobe failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var streams = new List<ProbedStream>();
        foreach (var stream in document.RootElement.GetProperty("streams").EnumerateArray())
        {
            Assert.Equal("hevc", stream.GetProperty("codec_name").GetString());
            streams.Add(new ProbedStream(
                stream.GetProperty("profile").GetString()!,
                stream.GetProperty("pix_fmt").GetString()!,
                stream.GetProperty("width").GetInt32(),
                stream.GetProperty("height").GetInt32(),
                stream.GetProperty("color_range").GetString()!,
                int.Parse(stream.GetProperty("nb_read_frames").GetString()!, CultureInfo.InvariantCulture)));
        }

        return streams;
    }

    /// <summary>
    /// Decodes the pictures of a stream with the HEVC decoder of the library, as the container gives them to it (the
    /// parameter sets of the configuration record, then the data of each item or sample), and compares their planes,
    /// cropped to the conformance window, with the planes of the reference decoders.
    /// </summary>
    private static void AssertLibraryPlanes(string name, ReferenceHeif.HevcConfiguration configuration, IReadOnlyList<byte[]> samples, IReadOnlyList<ReferencePlanes> expected)
    {
        Assert.Equal(4, configuration.NalUnitLengthSize);
        var scope = AllocationScope.Create(ImageConfiguration.Default, name);
        using var decoder = new HevcDecoder(scope);
        var pictures = new List<ushort[][]>();
        for (var i = 0; i < samples.Count; i++)
        {
            var data = i is 0 ? [.. ReferenceHevcStream.ToLengthPrefixed(configuration.NalUnits), .. samples[i]] : samples[i];
            decoder.SetInput(data, 0, data.Length, nalLengthSize: 4);
            Drain();
        }

        decoder.EndOfSequence();
        Drain();
        Assert.HasCount(expected.Count, pictures, $"{name}: the number of pictures that the library decodes.");
        for (var i = 0; i < expected.Count; i++)
        {
            ushort[][] reference = expected[i].IsMonochrome ? [expected[i].Y] : [expected[i].Y, expected[i].U, expected[i].V];
            Assert.HasCount(reference.Length, pictures[i], $"{name}: the number of planes of the picture {i}.");
            for (var plane = 0; plane < reference.Length; plane++)
            {
                if (!reference[plane].AsSpan().SequenceEqual(pictures[i][plane]))
                    Assert.Fail(string.Create(CultureInfo.InvariantCulture, $"{name}: the plane {plane} of the picture {i} that the library decodes differs from the plane of the reference decoders ({expected[i].Decoders}) at the sample {reference[plane].AsSpan().CommonPrefixLength(pictures[i][plane])} of {reference[plane].Length} and {pictures[i][plane].Length}."));
            }
        }

        void Drain()
        {
            while (decoder.DecodeNextOutputPicture(XunitCancellationToken) is { } picture)
            {
                try
                {
                    var sps = picture.Sps;
                    var planes = new ushort[picture.PlaneCount][];
                    for (var plane = 0; plane < planes.Length; plane++)
                    {
                        var (subWidth, subHeight) = plane is 0 ? (1, 1) : (sps.SubWidthC, sps.SubHeightC);
                        var (left, top, width, height) = (sps.OutputLeft / subWidth, sps.OutputTop / subHeight, sps.OutputWidth / subWidth, sps.OutputHeight / subHeight);
                        var samplesOfPlane = picture.GetPlane(plane);
                        planes[plane] = new ushort[width * height];
                        for (var y = 0; y < height; y++)
                        {
                            samplesOfPlane.GetRow(top + y).Slice(left, width).CopyTo(planes[plane].AsSpan(y * width));
                        }
                    }

                    pictures.Add(planes);
                }
                finally
                {
                    picture.Release();
                }
            }
        }
    }

    /// <summary>
    /// The pixels of a source as a file of <paramref name="bitDepth"/> bits stores them without loss, on the 16-bit scale of
    /// the library: a 16-bit sample <c>v</c> (an 8-bit sample is multiplied by 257) is coded as the nearest sample of the bit
    /// depth, <c>round(v * max / 65535)</c>, which the library scales back to <c>round(q * 65535 / max)</c>.
    /// </summary>
    private static RawPixelBuffer ToCodedPixels(RawPixelBuffer source, int bitDepth)
    {
        var maximum = (1 << bitDepth) - 1;
        var result = ToRgba16(source).Span.ToArray();
        for (var i = 0; i < result.Length; i += 2)
        {
            var coded = ((BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(i)) * (long)maximum) + 32767) / 65535;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i), (ushort)(((coded * 65535) + (maximum / 2)) / maximum));
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Rgba16Le, result);
    }

    /// <summary>Returns <paramref name="whole"/> with the pixels of <paramref name="image"/>, which is its top-left part.</summary>
    private static RawPixelBuffer Overlay(RawPixelBuffer whole, RawPixelBuffer image)
    {
        var result = whole.Span.ToArray();
        for (var y = 0; y < image.Height; y++)
        {
            image.Span.Slice(y * image.Width * 8, image.Width * 8).CopyTo(result.AsSpan(y * whole.Width * 8));
        }

        return RawPixelBuffer.Create(whole.Width, whole.Height, RawPixelLayout.Rgba16Le, result);
    }

    private static Image CreateImage(RawPixelBuffer pixels) => EncoderSources.CreateImage(pixels, pixels.Layout.Name switch
    {
        "rgb8" => PixelFormat.Rgb24,
        "rgba8" => PixelFormat.Rgba32,
        "gray8" => PixelFormat.Gray8,
        "gray16le" => PixelFormat.Gray16,
        _ => PixelFormat.Rgba64,
    });

    private static FullPath ArtifactPath(string fileName) => InteropSettings.GetArtifactsDirectory(nameof(HeicEncoderInteropTests)) / fileName;
}
