using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Heif;
using Meziantou.Framework.Imaging.TestHarness.Hevc;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

// The files that libheif does not write: HEVC streams of the x265 command-line tool in a container assembled by the test
// harness, without any color description, and the files of Apple software (macOS only).
public sealed partial class HeicDecoderInteropTests
{
    /// <summary>
    /// The bound of matrixed samples against Apple ImageIO, which returns 8-bit samples computed with its own fixed-point
    /// matrix: three 8-bit steps, the bound of the AVIF suite (measured here: 2 steps on macOS 27).
    /// </summary>
    private const int ImageIOMatrixBound = 3 * 257;

    /// <summary>
    /// Images without <c>colr</c> box, whose bitstream has no video signal description or part of one: x265 encodes raw
    /// planes with the options of the case, and <see cref="HeifFileBuilder"/> wraps the stream in a file that has nothing
    /// but the properties an image requires. The library documents its defaults: the limited range without video signal
    /// type (the value that ITU-T H.265 infers), and the BT.601 matrix for unspecified matrix coefficients; it reports a
    /// color description only when the bitstream has one. The case states what libheif does, which never expands the
    /// samples of a monochrome image: its pixels are compared with the planes converted with that matrix and range.
    /// </summary>
    /// <param name="label">The name of the case.</param>
    /// <param name="chromaFormatIdc">The chroma format of the planes.</param>
    /// <param name="options">The x265 options that write the video signal description.</param>
    /// <param name="matrix">The matrix coefficients of the image for the library; 2 (unspecified) is converted as BT.601.</param>
    /// <param name="fullRange">Whether the library reads the samples as full range samples.</param>
    /// <param name="reportsColorDescription">Whether the library reports a color description.</param>
    /// <param name="libHeifMatrix">The matrix coefficients that heif-dec converts with.</param>
    /// <param name="libHeifFullRange">Whether heif-dec reads the samples as full range samples.</param>
    [Theory]
    [InlineData("no-signal-444", 3, new string[0], 2, false, false, 6, false)]
    [InlineData("no-signal-420", 1, new string[0], 2, false, false, 6, false)]
    [InlineData("no-signal-400", 0, new string[0], 2, false, false, 6, true)]
    [InlineData("limited-range-444", 3, new[] { "--range", "limited" }, 2, false, false, 6, false)]
    [InlineData("limited-range-400", 0, new[] { "--range", "limited" }, 2, false, false, 6, true)]
    [InlineData("full-range-444", 3, new[] { "--range", "full" }, 2, true, false, 6, true)]
    [InlineData("bt709-444", 3, new[] { "--colormatrix", "bt709" }, 1, false, true, 1, false)]
    [InlineData("bt709-full-420", 1, new[] { "--colormatrix", "bt709", "--range", "full" }, 1, true, true, 1, true)]
    [InlineData("primaries-only-444", 3, new[] { "--colorprim", "bt709" }, 2, false, true, 6, false)]
    public async Task ColorDefaultsApplyWithoutColorBox(string label, int chromaFormatIdc, string[] options, int matrix, bool fullRange, bool reportsColorDescription, int libHeifMatrix, bool libHeifFullRange)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "no-colr-" + label;
        var path = await CreateFileWithoutColorBoxAsync(name, chromaFormatIdc, options);

        // The library against the planes, converted by the rule it documents
        var still = await AssertStillAsync(heif, path, name, HeifDecComparison.Exact, compareWithHeifDec: false);
        Assert.Null(still.Reader.PrimaryItem!.Nclx);
        Assert.Equal((matrix, fullRange), ResolveColor(nclx: null, still.Color.Signal));
        Assert.Equal(reportsColorDescription, still.Cicp is not null);

        // heif-dec against the planes, converted with each matrix and range it could have chosen
        var decoded = (await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken)).GetPixels(8);
        var matches = new List<(int Matrix, bool FullRange)>();
        var measures = new List<string>();
        foreach (var candidateMatrix in still.Color.IsMonochrome ? [6] : new[] { 1, 6, 9 })
        {
            foreach (var candidateRange in new[] { true, false })
            {
                var measure = MeasurePixels(ConvertToPixels(still.Color, alpha: null, candidateMatrix, candidateRange, still.Crop, ChromaModel.LibHeif), decoded);
                measures.Add(string.Create(CultureInfo.InvariantCulture, $"matrix {candidateMatrix} {(candidateRange ? "full" : "limited")}: {measure.Max}"));
                if (measure.Max <= ColorBound.Matrix(8, candidateRange))
                {
                    matches.Add((candidateMatrix, candidateRange));
                }
            }
        }

        var difference = MeasurePixels(decoded, still.Pixels);
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: library converts with matrix {matrix} {(fullRange ? "full" : "limited")}; heif-dec vs the planes converted with {string.Join(", ", measures)}; library vs heif-dec max {difference.Max} ({difference.Max / 257.0:F3}/255)"));
        Assert.Equal([(libHeifMatrix, libHeifFullRange)], matches);

        // For information, on macOS: the matrix and the range that explain the pixels of Apple ImageIO, for the planes that
        // need no chroma upsampling
        if (OperatingSystem.IsMacOS() && chromaFormatIdc != 1)
        {
            var (swift, script) = RequiredTools.AppleImageIO();
            var result = await ProcessRunner.RunAsync(swift, [script, still.Color.IsMonochrome ? "--opaque" : "--binary-alpha", path], XunitCancellationToken);
            if (result.ExitCode != 0 || result.StandardOutput.Length != RawPixelLayout.Rgba8.GetByteLength(X265Width, X265Height))
            {
                WriteOutput($"{name}: Apple ImageIO does not decode the file (exit code {result.ExitCode}): {result.StandardError.Trim()}");
                return;
            }

            var apple = ToRgba16(RawPixelBuffer.Create(X265Width, X265Height, RawPixelLayout.Rgba8, result.StandardOutput.Span));
            var appleMeasures = new List<string>();
            foreach (var candidateMatrix in still.Color.IsMonochrome ? [6] : new[] { 1, 6, 9 })
            {
                foreach (var candidateRange in new[] { true, false })
                {
                    var measure = MeasurePixels(ConvertToPixels(still.Color, alpha: null, candidateMatrix, candidateRange, still.Crop, ChromaModel.LibHeif), apple);
                    appleMeasures.Add(string.Create(CultureInfo.InvariantCulture, $"matrix {candidateMatrix} {(candidateRange ? "full" : "limited")}: {measure.Max}{(measure.Max <= ImageIOMatrixBound ? " (explains)" : "")}"));
                }
            }

            var appleDifference = MeasurePixels(apple, still.Pixels);
            WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: Apple ImageIO vs the planes converted with {string.Join(", ", appleMeasures)}; library vs Apple ImageIO max {appleDifference.Max} ({appleDifference.Max / 257.0:F3}/255)"));
        }
    }

    /// <summary>
    /// Encodes deterministic planes with the x265 command-line tool and wraps the stream in an HEIC file without <c>colr</c>
    /// box: an <c>hvc1</c> item with its <c>ispe</c> and <c>hvcC</c> properties, the parameter sets in the configuration record.
    /// </summary>
    private static async Task<FullPath> CreateFileWithoutColorBoxAsync(string name, int chromaFormatIdc, string[] options)
    {
        var (data, configuration) = await EncodeItemWithX265Async(name, chromaFormatIdc, options);
        var builder = CreateStillBuilder();
        builder.AddItem(1, "hvc1", data, builder.AddProperty(HeifFileBuilder.ImageSize(X265Width, X265Height)), -builder.AddProperty(configuration));
        var path = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);
        return path;
    }

    /// <summary>A builder of an HEIC file whose primary item is the item 1.</summary>
    private static HeifFileBuilder CreateStillBuilder()
    {
        var builder = new HeifFileBuilder { PrimaryItemId = 1, MajorBrand = "heic" };
        builder.CompatibleBrands.Clear();
        builder.CompatibleBrands.Add("mif1");
        builder.CompatibleBrands.Add("heic");
        return builder;
    }

    private const int X265Width = 80;
    private const int X265Height = 64;

    /// <summary>
    /// Encodes deterministic planes of 80x64 samples with the x265 command-line tool, which writes the video signal
    /// description its options ask for and nothing else, for a coded item: the data of the item (the NAL units of the
    /// picture, preceded by their length) and its <c>hvcC</c> property, which holds the parameter sets.
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static async Task<(byte[] Data, byte[] Configuration)> EncodeItemWithX265Async(string name, int chromaFormatIdc, string[] options)
    {
        const int Width = X265Width;
        const int Height = X265Height;
        var encoder = await RequiredTools.HevcReferenceEncoderAsync(HevcReferenceEncoder.X265);

        // Planes of gradients, of saturated areas and of noise, which cover the range of the samples
        var (chromaWidth, chromaHeight) = chromaFormatIdc switch
        {
            0 => (0, 0),
            1 => (Width / 2, Height / 2),
            2 => (Width / 2, Height),
            _ => (Width, Height),
        };
        var planes = new byte[(Width * Height) + (2 * chromaWidth * chromaHeight)];
        var random = new Random(StableSeed(name));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                planes[(y * Width) + x] = (byte)Math.Clamp((x * 255 / (Width - 1)) + random.Next(-6, 7), 0, 255);
            }
        }

        for (var plane = 0; plane < 2; plane++)
        {
            var offset = (Width * Height) + (plane * chromaWidth * chromaHeight);
            for (var y = 0; y < chromaHeight; y++)
            {
                for (var x = 0; x < chromaWidth; x++)
                {
                    var gradient = plane == 0 ? y * 255 / (chromaHeight - 1) : 255 - ((x + y) * 255 / (chromaWidth + chromaHeight - 2));
                    var value = x >= chromaWidth / 4 && x < chromaWidth / 2 && y >= chromaHeight / 4 && y < chromaHeight / 2 ? (plane == 0 ? 16 : 240) : gradient;
                    planes[offset + (y * chromaWidth) + x] = (byte)Math.Clamp(value + random.Next(-4, 5), 0, 255);
                }
            }
        }

        var planesPath = ArtifactPath(name + ".yuv");
        var streamPath = ArtifactPath(name + ".x265.hevc");
        await File.WriteAllBytesAsync(planesPath, planes, XunitCancellationToken);
        File.Delete(streamPath);
        var error = await encoder.EncodeAsync(encoder.GetArguments(planesPath, streamPath, Width, Height, chromaFormatIdc, bitDepth: 8, frameCount: 1, ["--qp", "20", .. options]), TimeSpan.FromMinutes(1), XunitCancellationToken);
        Assert.Null(error);

        var units = ReferenceHevcStream.SplitAnnexB(await File.ReadAllBytesAsync(streamPath, XunitCancellationToken));
        byte[][] Select(int type) => [.. units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) == type).Select(unit => unit.ToArray())];
        var sequenceParameterSet = Assert.Single(Select(ReferenceHevcStream.NalSps));

        // The general profile, tier and level of the record are those of the sequence parameter set: the twelve bytes that
        // follow sps_video_parameter_set_id, sps_max_sub_layers_minus1 and sps_temporal_id_nesting_flag
        var profileTierLevel = ReferenceHevcStream.GetRbsp(sequenceParameterSet).AsSpan(1, 12).ToArray();
        return (
            ReferenceHevcStream.ToLengthPrefixed(units.Where(unit => ReferenceHevcStream.GetNalUnitType(unit.Span) is not (ReferenceHevcStream.NalVps or ReferenceHevcStream.NalSps or ReferenceHevcStream.NalPps))),
            HeifFileBuilder.HevcConfiguration(chromaFormatIdc, 8, 8, [(ReferenceHevcStream.NalVps, Select(ReferenceHevcStream.NalVps)), (ReferenceHevcStream.NalSps, [sequenceParameterSet]), (ReferenceHevcStream.NalPps, Select(ReferenceHevcStream.NalPps))], profileTierLevel: profileTierLevel));
    }

    /// <summary>
    /// The range of the samples of an alpha plane is the range of its own bitstream, which is full when the bitstream does
    /// not signal one: x265 encodes a monochrome plane with the video signal description of the case, and
    /// <see cref="HeifFileBuilder"/> attaches it to a color image as its alpha auxiliary image. MIAF requires alpha planes in
    /// full range (ISO/IEC 23000-22, section 7.3.5.2), and libheif reads every alpha plane as such; the library expands the
    /// samples of a plane that declares the limited range.
    /// </summary>
    [Theory]
    [InlineData("no-signal", new string[0], true)]
    [InlineData("full-range", new[] { "--range", "full" }, true)]
    [InlineData("limited-range", new[] { "--range", "limited" }, false)]
    public async Task AlphaPlaneFollowsTheRangeOfItsBitstream(string label, string[] options, bool fullRange)
    {
        var heif = await RequiredTools.LibHeifAsync();
        var name = "alpha-range-" + label;
        var color = await EncodeItemWithX265Async(name + "-color", chromaFormatIdc: 3, ["--range", "full", "--colormatrix", "bt709"]);
        var alpha = await EncodeItemWithX265Async(name + "-alpha", chromaFormatIdc: 0, options);
        var builder = CreateStillBuilder();
        var size = builder.AddProperty(HeifFileBuilder.ImageSize(X265Width, X265Height));
        builder.AddItem(1, "hvc1", color.Data, size, -builder.AddProperty(color.Configuration));
        builder.AddItem(2, "hvc1", alpha.Data, size, -builder.AddProperty(alpha.Configuration), -builder.AddProperty(HeifFileBuilder.AuxiliaryType(ReferenceHeif.HevcAlphaAuxiliaryType)));
        builder.References.Add(("auxl", 2, [1]));
        var path = ArtifactPath(name + ".heic");
        await File.WriteAllBytesAsync(path, builder.Build(), XunitCancellationToken);

        // The library against the planes: the alpha plane is scaled from the range that its bitstream declares
        var still = await AssertStillAsync(heif, path, name, HeifDecComparison.Matrix, compareWithHeifDec: false);
        Assert.NotNull(still.Alpha);
        Assert.Equal(fullRange, AlphaFullRange(still.Alpha.Signal));
        Assert.Equal(options.Length > 0, still.Alpha.Signal.HasVideoSignalType);

        // heif-dec against the planes, with the alpha plane read in each range
        var decoded = (await heif.DecodeAsync(path, bilinearChroma: true, XunitCancellationToken)).GetPixels(8);
        var (matrix, colorFullRange) = ResolveColor(nclx: null, still.Color.Signal);
        var matches = new List<bool>();
        foreach (var candidate in new[] { true, false })
        {
            var planes = still.Alpha with { Signal = still.Alpha.Signal with { HasVideoSignalType = true, FullRange = candidate } };
            if (MeasurePixels(ConvertToPixels(still.Color, planes, matrix, colorFullRange, still.Crop, ChromaModel.LibHeif), decoded).FirstAlphaMismatch is null)
            {
                matches.Add(candidate);
            }
        }

        WriteOutput($"{name}: the library reads the alpha plane as {(fullRange ? "full" : "limited")} range samples; heif-dec {heif.Version} as {string.Join(" or ", matches.Select(match => match ? "full" : "limited"))} range samples");
        Assert.Equal([true], matches);
    }

    /// <summary>
    /// Files written by Apple software (<c>sips -s format heic</c>, which uses the HEVC encoder of the system): an 8-bit
    /// 4:2:0 image, a 10-bit one from a 16-bit source, 4:4:4 planes at the highest quality, an alpha plane (a monochrome
    /// image whose picture is larger than its conformance window), a gray source (4:2:0 planes with the ICC profile of a
    /// gray color space), and a grid of 512x512 tiles for a large image. Every file is compared with the planes of the
    /// reference decoders and with heif-dec, like the files of heif-enc, and with Apple ImageIO
    /// (<c>imageio_frames.swift</c>), which returns 8-bit samples:
    /// <list type="bullet">
    /// <item><description>4:4:4 and gray samples are within <see cref="ImageIOMatrixBound"/> of the pixels of the library.</description></item>
    /// <item><description>
    /// Subsampled chroma is not compared: ImageIO does not upsample it the same way for every file. The differences with
    /// the planes converted by each chroma model (the position the stream declares, centered samples, replicated samples)
    /// are written to the test output. On macOS 27, ImageIO replicates the chroma samples of the files that have a clean
    /// aperture (it is within one 8-bit step of the replicated samples), and interpolates those of the other files with a
    /// filter that no model reproduces: the model of the library is the closest (1.6 step on average, 45 at most).
    /// </description></item>
    /// </list>
    /// </summary>
    /// <param name="label">The name of the case.</param>
    /// <param name="kind">The layout of the PNG source.</param>
    /// <param name="width">The width of the source.</param>
    /// <param name="height">The height of the source.</param>
    /// <param name="options">The sips options.</param>
    /// <param name="comparison">How the library compares with heif-dec.</param>
    /// <param name="imageIO">
    /// How the pixels of ImageIO are obtained and compared: <c>direct</c>, <c>opaque</c> (a gray image), <c>models</c>
    /// (subsampled chroma: measured only) or <c>none</c> (an alpha plane, which the helper only reads when ImageIO returns
    /// straight alpha, or 16-bit samples).
    /// </param>
    [Theory]
    [InlineData("rgb-8", SourceKind.Rgb8, 96, 64, new string[0], HeifDecComparison.SitingDiffers, "models")]
    [InlineData("rgb-8-odd", SourceKind.Rgb8, 65, 47, new string[0], HeifDecComparison.SitingDiffers, "models")]
    [InlineData("rgb-8-low-quality", SourceKind.Rgb8, 96, 64, new[] { "-s", "formatOptions", "20" }, HeifDecComparison.SitingDiffers, "models")]
    [InlineData("rgb-8-444", SourceKind.Rgb8, 96, 64, new[] { "-s", "formatOptions", "100" }, HeifDecComparison.Matrix, "direct")]
    [InlineData("rgb-8-444-odd", SourceKind.Rgb8, 65, 47, new[] { "-s", "formatOptions", "100" }, HeifDecComparison.Matrix, "direct")]
    [InlineData("rgba-8", SourceKind.Rgba8, 96, 64, new string[0], HeifDecComparison.SitingDiffers, "none")]
    [InlineData("rgba-8-odd", SourceKind.Rgba8, 65, 47, new string[0], HeifDecComparison.SitingDiffers, "none")]
    [InlineData("rgb-16", SourceKind.Rgb16, 96, 64, new string[0], HeifDecComparison.SitingDiffers, "none")]
    [InlineData("gray-8", SourceKind.Gray8, 96, 64, new string[0], HeifDecComparison.SitingDiffers, "opaque")]
    [InlineData("grid", SourceKind.Rgb8, 1100, 700, new string[0], HeifDecComparison.SitingDiffers, "models")]
    public async Task FilesWrittenByAppleDecodeAsTheReferences(string label, SourceKind kind, int width, int height, string[] options, HeifDecComparison comparison, string imageIO)
    {
        // Apple ImageIO and sips exist only on macOS: skipped elsewhere locally; required (never skipped) in the CI interop job (macOS)
        var (swift, script) = RequiredTools.AppleImageIO();
        var heif = await RequiredTools.LibHeifAsync();
        var name = "apple-" + label;
        var source = CreateSource(kind, width, height, seed: StableSeed(name));
        var sourcePath = await SaveSourceAsync(name, source);
        var path = ArtifactPath(name + ".heic");
        File.Delete(path);
        var sips = await ProcessRunner.RunAsync("/usr/bin/sips", ["-s", "format", "heic", .. options, sourcePath, "--out", path], XunitCancellationToken);
        if (sips.ExitCode != 0 || !File.Exists(path))
            Assert.Fail($"sips failed on {name} (exit code {sips.ExitCode}): {sips.StandardError}{sips.StandardOutputText}");

        var still = await AssertStillAsync(heif, path, name, comparison);
        Assert.Equal((width, height), (still.Width, still.Height));
        var primary = still.Reader.PrimaryItem!;
        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: brands {still.Reader.MajorBrand} [{string.Join(' ', still.Reader.CompatibleBrands)}], primary item '{primary.Type}' [{string.Join(' ', primary.Associations.Select(association => association.Property.Type))}]{(still.Reader.AlphaItem is { } alphaItem ? $", alpha item [{string.Join(' ', alphaItem.Associations.Select(association => association.Property.Type))}] of {alphaItem.Size}" : "")}, picture of {still.Color.Width}x{still.Color.Height}, nclx {primary.Nclx}, ICC profile of {primary.IccProfile?.Length ?? 0} bytes"));
        if (kind is SourceKind.Gray8)
        {
            // A gray image of Apple is coded as 4:2:0 planes with the ICC profile of a gray color space, which cannot label
            // the RGB pixels the planes convert to: the library returns the pixels without the profile
            Assert.False(still.Color.IsMonochrome);
            Assert.Equal("GRAY", Encoding.ASCII.GetString(primary.IccProfile!.Value.Span.Slice(16, 4)));
            using var image = Image.Load(path);
            Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
            Assert.Null(image.Metadata.IccProfile);
        }

        if (imageIO == "none")
            return;

        var result = await ProcessRunner.RunAsync(swift, [script, imageIO == "opaque" ? "--opaque" : "--binary-alpha", path], XunitCancellationToken);
        if (result.ExitCode != 0)
            Assert.Fail($"ImageIO failed on {name} (exit code {result.ExitCode}): {result.StandardError}");

        // "frames <count> <width> <height> <bits>": straight RGBA, opaque. It is the last line of the error output: the
        // system frameworks can write diagnostics before it
        var header = result.StandardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1].Split(' ');
        Assert.Equal(["frames", "1", width.ToString(CultureInfo.InvariantCulture), height.ToString(CultureInfo.InvariantCulture), "8"], header);
        var apple = ToRgba16(RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba8, result.StandardOutput.Span));
        var direct = MeasurePixels(apple, still.Pixels);
        if (imageIO != "models")
        {
            AssertPixels(apple, still.Pixels, ImageIOMatrixBound, $"{name}: library vs Apple ImageIO");
            WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: library vs Apple ImageIO max {direct.Max} ({direct.Max / 257.0:F3}/255), mean {direct.Mean:F2}, bound {ImageIOMatrixBound}"));
            return;
        }

        // Which upsampling of the chroma planes gives the pixels of ImageIO
        var (matrix, fullRange) = ResolveColor(primary.Nclx, still.Color.Signal);
        var measures = new List<string>();
        var explained = new List<ChromaModel>();
        foreach (var model in new[] { ChromaModel.Declared, ChromaModel.LibHeif, ChromaModel.LibHeifSequence })
        {
            var measure = MeasurePixels(ConvertToPixels(still.Color, still.Alpha, matrix, fullRange, still.Crop, model), apple);
            measures.Add(string.Create(CultureInfo.InvariantCulture, $"{model}: max {measure.Max} ({measure.Max / 257.0:F3}/255), mean {measure.Mean:F2}"));
            if (measure.Max <= ImageIOMatrixBound + ColorBound.Step(8))
            {
                explained.Add(model);
            }
        }

        WriteOutput(string.Create(CultureInfo.InvariantCulture, $"{name}: Apple ImageIO vs the planes converted with the chroma samples as {string.Join("; ", measures)}; explained by: {(explained.Count == 0 ? "none" : string.Join(", ", explained))}; library vs Apple ImageIO max {direct.Max} ({direct.Max / 257.0:F3}/255), mean {direct.Mean:F2}"));
    }
}
