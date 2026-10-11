using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Differential tests of the JPEG XL decoder on what is not an exact copy of integer samples: frames coded with VarDCT,
/// lossy Modular frames, the XYB color space and its conversions, the restoration filters, upsampling, patches, splines,
/// noise, LF frames, JPEG data transcoded losslessly, and the composition of layers and animations. Files are written at
/// test time by the pinned <c>cjxl</c> and <c>jxl_from_tree</c>; no file is committed.
/// </summary>
/// <remarks>
/// <para>
/// The format defines these samples in real numbers, and decoders agree within rounding. The expectation is the
/// floating-point output of two reference decoders that share no code, libjxl's <c>djxl</c> and jxl-oxide: the library
/// must be within a tolerance of each, plus half a unit of its own integer depth, and the two references must agree with
/// each other, which shows that the tolerance is not hiding a misreading of the file.
/// </para>
/// <para>
/// Tolerances are fractions of the range. libjxl approximates powers, transfer functions and the error function, which
/// costs it up to 0.0005 on hard synthetic edges; jxl-oxide is closer. A case that needs more says why.
/// </para>
/// </remarks>
public sealed class JpegXlLossyDecoderInteropTests
{
    private const double DjxlTolerance = 0.0008;
    private const double OxideTolerance = 0.0004;

    /// <summary>A reference decoder is not compared: the case says why.</summary>
    private const double Skip = -1;

    /// <summary>The options of cjxl that reach the coding tools of the lossy modes.</summary>
    public static TheoryData<string, string, double, double> EncoderCases
    {
        get
        {
            var data = new TheoryData<string, string, double, double>();
            void Add(string pattern, string options, double djxl = DjxlTolerance, double oxide = OxideTolerance) => data.Add(pattern, options, djxl, oxide);

            // The distance selects the quantization and, from 10 on, upsampling; the effort selects the transforms that the
            // encoder tries, from the DCT of 8x8 samples only to all of them
            foreach (var distance in new[] { "0.3", "1", "3", "6", "12", "20" })
            {
                Add("photo-rgb16", $"-d {distance}");
            }

            foreach (var effort in new[] { 1, 2, 3, 4, 5, 6, 8, 9 })
            {
                Add("photo-rgb16", $"-d 1.5 -e {effort}");
            }

            // The restoration filters, alone and together
            Add("photo-rgb16", "-d 2 --gaborish=0 --epf=0");
            Add("photo-rgb16", "-d 2 --gaborish=1 --epf=0");
            Add("photo-rgb16", "-d 2 --gaborish=0 --epf=1");
            Add("photo-rgb16", "-d 2 --gaborish=0 --epf=2");
            Add("photo-rgb16", "-d 2 --gaborish=0 --epf=3");
            Add("photo-rgb16", "-d 2 --gaborish=1 --epf=3");

            // Passes, LF frames (the LF image is a frame of its own, once or twice), the order of the groups
            Add("photo-rgb16", "-d 1 -p");
            Add("photo-rgb16", "-d 1 --progressive_ac");
            Add("photo-rgb16", "-d 1 --qprogressive_ac");
            Add("photo-rgb16", "-d 1 --progressive_dc=1");
            Add("photo-rgb16", "-d 1 --progressive_dc=2");
            Add("photo-rgb16", "-d 1 --group_order=1");
            Add("photo-rgb16", "-d 1 --faster_decoding=2");
            Add("photo-rgb16", "-d 1 --faster_decoding=4");

            // Upsampling of the color channels and of the alpha channel, by the same factor or not
            Add("photo-rgb16", "-d 1 --resampling=2");
            Add("photo-rgb16", "-d 1 --resampling=4");
            Add("photo-rgb16", "-d 1 --resampling=8");
            Add("photo-rgba16", "-d 1 --ec_resampling=2");
            Add("photo-rgba16", "-d 1 --resampling=2 --ec_resampling=2");
            Add("photo-rgba16", "-d 1 --resampling=2 --ec_resampling=4");
            Add("photo-rgba16", "-d 0 --ec_resampling=2");
            Add("photo-rgb16", "-d 0 --resampling=2");

            // Synthesized noise, at the resolution of the image
            Add("photo-rgb16", "-d 1 --photon_noise_iso=3200");
            Add("photo-rgb16", "-d 2 --photon_noise_iso=800 --resampling=2", oxide: Skip); // jxl-oxide generates the noise before upsampling
            Add("photo-rgb16", "-d 1 -m 1 --photon_noise_iso=1600");

            // Lossy Modular frames: XYB samples as integers, the squeeze transform, the filters with a constant strength
            Add("photo-rgb16", "-d 1 -m 1");
            Add("photo-rgb16", "-d 4 -m 1");
            Add("photo-rgb16", "-d 1 -m 1 -p");
            Add("photo-gray16", "-d 1 -m 1");
            Add("photo-rgba16", "-d 1 -m 1");
            Add("photo-rgb8", "-d 2 -m 1");

            // Depths, grayscale and alpha, which is lossless unless the encoder is told otherwise
            Add("photo-rgb8", "-d 1");
            Add("photo-rgb8", "-d 3 -e 3");
            Add("photo-gray16", "-d 1");
            Add("photo-gray8", "-d 1");
            Add("photo-rgba16", "-d 1");
            Add("photo-rgba16", "-d 1 -a 2");
            Add("photo-rgba16", "-d 1 --keep_invisible=1");
            Add("photo-rgba8", "-d 1");

            // One group and one section; several LF groups
            Add("photo-small-rgb16", "-d 1");
            Add("photo-small-rgb16", "-d 0.1");
            Add("photo-small-rgb16", "-d 8");
            Add("photo-wide-rgb16", "-d 2");
            Add("photo-wide-rgb16", "-d 2 --progressive_dc=1");

            // The color encoding the image declares: the samples are returned in it, whatever their range. jxl-oxide brings the
            // colors that the pattern has outside the gamut of the encoding back into it, and tone maps the images whose
            // intensity target is above the one of a standard display: it is not compared there
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_DCI_Rel_SRG", oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_DCI_DCI_Rel_DCI", oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_SRG_Rel_709");
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_SRG_Rel_Lin");

            // The PQ curve is so steep near black that the rounding of a linear sample moves its encoding by a hundredth
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_202_Rel_PeQ", djxl: 0.02, oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_202_Rel_HLG", oxide: Skip);
            Add("photo-gray16", "-d 1 -x color_space=Gra_D65_Rel_Lin");
            Add("photo-gray16", "-d 1 -x color_space=Gra_D65_Rel_709");
            Add("photo-rgb16", "-d 1 --intensity_target=1000", oxide: Skip);

            // Encodings without code points: a power function, custom primaries, a custom white point. The library describes
            // them with an ICC profile and returns the samples in them. A power function is as steep near black as PQ
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_SRG_Rel_g0.4545", djxl: 0.02, oxide: 0.02);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_D65_DCI_Per_g0.5555", djxl: 0.02, oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_DCI_SRG_Rel_DCI", djxl: 0.02, oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_0.32;0.33_0.67;0.32;0.21;0.71;0.14;0.08_Rel_g0.5", djxl: 0.02, oxide: Skip);
            Add("photo-rgb8", "-d 1 -x color_space=RGB_D65_0.64;0.33;0.21;0.71;0.15;0.06_Rel_g0.4547", djxl: 0.02, oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_0.3333;0.3333_202_Rel_709", oxide: Skip);
            Add("photo-rgb16", "-d 1 -x color_space=RGB_0.3333;0.3333_SRG_Rel_Lin", oxide: Skip);
            Add("photo-gray16", "-d 1 -x color_space=Gra_D65_Rel_g0.4545", djxl: 0.02, oxide: 0.02);
            Add("photo-rgb16", "-d 0 -x color_space=RGB_0.32;0.33_0.67;0.32;0.21;0.71;0.14;0.08_Rel_g0.5");
            Add("photo-gray8", "-d 0 -x color_space=Gra_0.34;0.35_Per_709");

            // Patches: the encoder finds the repeated shapes, stores them in a frame that is not shown and refers to it
            Add("text-rgb8", "-d 1");
            Add("text-rgb8", "-d 3");
            Add("text-rgb8", "-d 1 -e 9");
            Add("text-rgb8", "-d 0 --patches=1");

            // Patches of a frame that is stored at half its size: they are copied before the frame is upsampled
            Add("text-rgb8", "-d 1 --resampling=2 --patches=1");
            Add("text-rgb8", "-d 0 --resampling=2 --patches=1");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncodedFilesDecodeLikeTheReferenceDecoders(string patternName, string options, double djxlTolerance, double oxideTolerance)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(EncodedFilesDecodeLikeTheReferenceDecoders));
        var name = $"{patternName}{options.Replace(' ', '_').Replace('=', '_')}";
        var inputPath = directory / (name + ".pam");
        var jxlPath = directory / (name + ".jxl");
        await File.WriteAllBytesAsync(inputPath, pattern.ToPam(), XunitCancellationToken);
        await jxl.EncodeAsync(inputPath, jxlPath, options.Split(' '), XunitCancellationToken);
        await AssertDecodesLikeTheReferencesAsync(jxlPath, djxlTolerance, oxideTolerance, $"{patternName} {options}");
    }

    /// <summary>
    /// JPEG files transcoded losslessly: the coefficients of the JPEG file are stored as they are, in YCbCr, with
    /// subsampled chroma channels and the quantization tables of the file.
    /// </summary>
    /// <remarks>
    /// With chroma from luma (only possible without subsampling), djxl differs from jxl-oxide and from the library by up
    /// to 0.1: it is not compared. The JPEG file itself, decoded by FFmpeg, is within the same distance of all three.
    /// </remarks>
    [Theory]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio444, "", Skip)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio444, "--jpeg_reconstruction_cfl=0", DjxlTolerance)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio422, "", DjxlTolerance)]
    [InlineData("photo-rgb8", JpegChromaSubsampling.Ratio420, "", DjxlTolerance)]
    [InlineData("photo-gray8", JpegChromaSubsampling.Auto, "", DjxlTolerance)]
    public async Task TranscodedJpegFilesDecodeLikeTheReferenceDecoders(string patternName, JpegChromaSubsampling subsampling, string options, double djxlTolerance)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get(patternName);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(TranscodedJpegFilesDecodeLikeTheReferenceDecoders));
        var name = $"{patternName}-{subsampling}{options.Replace('=', '_')}";
        var jpegPath = directory / (name + ".jpg");
        var jxlPath = directory / (name + ".jxl");
        using (var image = CreateImage(pattern, rowOffset: 0))
        {
            await image.SaveAsync(jpegPath, new JpegEncoder { Quality = 85, ChromaSubsampling = subsampling, AllowBitDepthReduction = true }, XunitCancellationToken);
        }

        string[] arguments = options.Length == 0 ? ["--lossless_jpeg=1"] : ["--lossless_jpeg=1", options];
        await jxl.EncodeAsync(jpegPath, jxlPath, arguments, XunitCancellationToken);
        var info = await jxl.GetInfoAsync(jxlPath, XunitCancellationToken);
        Assert.True(info.HasJpegReconstruction);
        Assert.False(info.IsXybEncoded);
        await AssertDecodesLikeTheReferencesAsync(jxlPath, djxlTolerance, OxideTolerance, name);
    }

    /// <summary>
    /// Animations converted from APNG files written by the library: every frame is blended over the previous one with its
    /// alpha channel and kept as a reference; lossy frames are coded with VarDCT.
    /// </summary>
    [Theory]
    [InlineData("-d 0")]
    [InlineData("-d 1")]
    [InlineData("-d 1 -m 1")]
    [InlineData("-d 2 --resampling=2")]
    public async Task AnimationsDecodeLikeTheReferenceDecoders(string options)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var pattern = JpegXlPattern.Get("photo-rgba8");
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AnimationsDecodeLikeTheReferenceDecoders));
        var name = "animation" + options.Replace(' ', '_').Replace('=', '_');
        var apngPath = directory / (name + ".apng");
        var jxlPath = directory / (name + ".jxl");
        int[] milliseconds = [100, 40, 250, 1000];
        using (var animation = CreateImage(pattern, rowOffset: 0))
        {
            for (var i = 1; i < milliseconds.Length; i++)
            {
                using var frame = CreateImage(pattern, rowOffset: i * 37);
                animation.AppendFrame(frame.Frames[0]);
            }

            for (var i = 0; i < milliseconds.Length; i++)
            {
                animation.Frames[i].Metadata.Duration = new FrameDuration(milliseconds[i], 1000);
            }

            await animation.SaveAsync(apngPath, new PngEncoder { AnimationMode = PngAnimationMode.Animated }, XunitCancellationToken);
        }

        await jxl.EncodeAsync(apngPath, jxlPath, options.Split(' '), XunitCancellationToken);
        var info = await jxl.GetInfoAsync(jxlPath, XunitCancellationToken);
        Assert.True(info.IsAnimation);

        // jxl-oxide differs from djxl on the color of the pixels that the blending leaves almost transparent
        using var image = await AssertDecodesLikeTheReferencesAsync(jxlPath, DjxlTolerance, Skip, name);
        Assert.Equal(milliseconds.Length, image.Frames.Count);
        for (var i = 0; i < milliseconds.Length; i++)
        {
            Assert.Equal(0, new FrameDuration(milliseconds[i], 1000).CompareTo(image.Frames[i].Metadata.Duration));
        }
    }

    /// <summary>
    /// Files that describe each sample with a decision tree, which reach what cjxl does not write: layers that are added
    /// or multiplied, at positions that leave the image, splines, upsampled Modular frames, YCbCr Modular frames, and the
    /// restoration filters on Modular frames.
    /// </summary>
    public static TheoryData<string, string, double, double> TreeCases
    {
        get
        {
            var data = new TheoryData<string, string, double, double>();
            void Add(string name, string tree, double djxl = DjxlTolerance, double oxide = OxideTolerance) => data.Add(name, tree, djxl, oxide);
            const string Gradient = """
                if c > 1
                  - Set 30
                  if c > 0
                    - W 1
                    - N 2
                """;

            // A still image made of layers: each one is combined with the frame before it, which is kept as a reference
            Add("layers-add-multiply", $"""
                Width 64
                Height 48
                Bitdepth 8
                NotLast
                {Gradient}
                Width 40
                Height 30
                FramePos 10 5
                BlendMode kAdd
                NotLast
                - Set 20
                Width 50
                Height 20
                FramePos -8 30
                BlendMode kMul
                NotLast
                - Set 128
                Width 30
                Height 60
                FramePos 40 -10
                BlendMode kReplace
                NotLast
                - Set 200
                Width 20
                Height 20
                FramePos 60 44
                BlendMode kAdd
                - Set 15
                """);

            // An animation whose frames are smaller than the image, with a layer between two of them
            Add("animation-crops", $"""
                Width 48
                Height 32
                Bitdepth 8
                Animation
                Duration 120
                NotLast
                {Gradient}
                Width 16
                Height 16
                FramePos 4 4
                BlendMode kAdd
                Duration 0
                NotLast
                - Set 40
                Width 24
                Height 12
                FramePos 20 18
                BlendMode kReplace
                Duration 30
                NotLast
                - Set 220
                Width 48
                Height 32
                FramePos 0 0
                BlendMode kMul
                Duration 500
                - Set 100
                """);

            // Splines, alone on a flat image: the libraries differ by the approximation of the error function of libjxl,
            // which jxl-oxide follows
            static string Coefficients(params double[] values) => string.Join(' ', Enumerable.Range(0, 32).Select(i => (i < values.Length ? values[i] : 0).ToString(CultureInfo.InvariantCulture)));
            Add("splines", $"""
                Width 256
                Height 200
                Bitdepth 16
                XYB
                Spline
                {Coefficients(0.005, 0.002)}
                {Coefficients(0.5, 0.2, -0.1)}
                {Coefficients(0.4, -0.2)}
                {Coefficients(3.0, 1.0, 0.5)}
                20 30 100 150 220 60 180 180
                EndSpline
                Spline
                {Coefficients(-0.004)}
                {Coefficients(0.3, 0, 0, 0.1)}
                {Coefficients(0.25)}
                {Coefficients(1.5)}
                10 190 120 20 250 190
                EndSpline
                - Set 40
                """, djxl: 0.006, oxide: 0.006);
            Add("splines-quantization", $"""
                Width 200
                Height 120
                Bitdepth 16
                XYB
                SplineQuantizationAdjustment 3
                Spline
                {Coefficients(0.0)}
                {Coefficients(0.4, 0.3)}
                {Coefficients(0.35)}
                {Coefficients(6.0, -2.0)}
                30 60 60 20 100 100 140 20 170 60
                EndSpline
                if y > 60
                  - Set 20
                  - Set 60
                """, djxl: 0.006, oxide: 0.006);

            // Splines of a frame that is stored at a lower resolution, with an alpha channel of that resolution: they are
            // drawn at that resolution, then upsampled with the frame
            foreach (var factor in new[] { 2, 4 })
            {
                Add($"splines-upsample-{factor}", $"""
                    Width 128
                    Height 100
                    Bitdepth 16
                    XYB
                    Alpha
                    Upsample {factor}
                    Upsample_EC {factor}
                    Spline
                    {Coefficients(0.005, 0.002)}
                    {Coefficients(0.5, 0.2, -0.1)}
                    {Coefficients(0.4, -0.2)}
                    {Coefficients(3.0, 1.0, 0.5)}
                    10 15 50 75 110 30 90 90
                    EndSpline
                    if c > 2
                      - Set 30000
                      if y > 20
                        - Set 40
                        - Set 80
                    """, djxl: 0.006, oxide: 0.006);
            }

            // Modular frames with what the lossless options of cjxl do not write
            foreach (var factor in new[] { 2, 4, 8 })
            {
                Add($"upsample-{factor}", $"""
                    Width 40
                    Height 28
                    Bitdepth 16
                    Upsample {factor}
                    if c > 1
                      - Set 9000
                      if y > 13
                        - W 700
                        - N 1100
                    """);
            }

            Add("ycbcr-modular", """
                Width 60
                Height 40
                Bitdepth 8
                CbYCr
                if c > 0
                  if c > 1
                    - Set 20
                    - W 2
                  - Set -30
                """);
            Add("xyb-modular-filters", """
                Width 80
                Height 64
                Bitdepth 16
                XYB
                Gaborish
                EPF 3
                if c > 0
                  if x > 40
                    - Set 60
                    - Set 0
                  if y > 30
                    - Set 200
                    - Set 90
                """);
            Add("xyb-noise", """
                Width 300
                Height 270
                Bitdepth 16
                XYB
                Noise 0.2 0.3 0.4 0.5 0.6 0.7 0.8 0.9
                if c > 0
                  - Set 0
                  - W 1
                """);
            Add("xyb-rec2100-pq", """
                Width 40
                Height 30
                Bitdepth 16
                XYB
                Rec2100
                PQ
                if c > 0
                  - Set 0
                  - W 3
                """, oxide: Skip);
            Add("squeeze-delta-palette", """
                Width 70
                Height 50
                Bitdepth 8
                Squeeze
                if c > 1
                  - Set 3
                  - W 1
                """);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(TreeCases))]
    public async Task TreeFilesDecodeLikeTheReferenceDecoders(string name, string tree, double djxlTolerance, double oxideTolerance)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var jxlPath = InteropSettings.GetArtifactsDirectory(nameof(TreeFilesDecodeLikeTheReferenceDecoders)) / (name + ".jxl");
        await jxl.EncodeFromTreeAsync(tree, jxlPath, XunitCancellationToken);
        using var image = await AssertDecodesLikeTheReferencesAsync(jxlPath, djxlTolerance, oxideTolerance, name);
    }

    /// <summary>Decodes a file with the library and with the reference decoders, and compares every sample of every frame.</summary>
    /// <returns>The image the library decoded.</returns>
    private static async Task<Image> AssertDecodesLikeTheReferencesAsync(FullPath path, double djxlTolerance, double oxideTolerance, string context)
    {
        var jxl = await RequiredTools.LibJxlAsync();
        var oxide = await RequiredTools.JxlOxideAsync();
        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var image = Image.Load(data);
        try
        {
            var djxlFrames = djxlTolerance >= 0 ? await jxl.DecodeFramesAsync(path, XunitCancellationToken) : null;
            var oxideFrames = oxideTolerance >= 0 ? await oxide.DecodeFramesAsync(path, XunitCancellationToken) : null;
            Assert.True(djxlFrames is not null || oxideFrames is not null, "A case is compared with at least one reference decoder.");
            if (djxlFrames is not null && oxideFrames is not null)
            {
                // The references agree: the tolerances are not hiding a file that is read in two ways
                Assert.Equal((djxlFrames.FrameCount, djxlFrames.Width, djxlFrames.Height, djxlFrames.ChannelCount), (oxideFrames.FrameCount, oxideFrames.Width, oxideFrames.Height, oxideFrames.ChannelCount));
                var worst = 0.0;
                for (var frame = 0; frame < djxlFrames.FrameCount; frame++)
                {
                    for (var y = 0; y < djxlFrames.Height; y++)
                    {
                        for (var x = 0; x < djxlFrames.Width; x++)
                        {
                            for (var c = 0; c < djxlFrames.ChannelCount; c++)
                            {
                                worst = Math.Max(worst, Math.Abs(Clamp(djxlFrames.GetSample(frame, x, y, c)) - Clamp(oxideFrames.GetSample(frame, x, y, c))));
                            }
                        }
                    }
                }

                Assert.True(worst <= djxlTolerance + oxideTolerance, $"{context}: djxl and jxl-oxide differ by {worst:F5}.");
            }

            if (djxlFrames is not null)
            {
                AssertFramesMatch(image, djxlFrames, djxlTolerance, $"{context}: djxl {jxl.Version}");
            }

            if (oxideFrames is not null)
            {
                AssertFramesMatch(image, oxideFrames, oxideTolerance, $"{context}: jxl-oxide {oxide.Version}");
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void AssertFramesMatch(Image image, NpyFrames reference, double tolerance, string context)
    {
        Assert.Equal((reference.FrameCount, reference.Width, reference.Height), (image.Frames.Count, image.Width, image.Height));

        // The library returns integers of 8 or 16 bits: half a unit of rounding is its own
        var is8Bit = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Rgb24 or PixelFormat.Rgba32 or PixelFormat.Bgra32;
        var allowed = tolerance + (0.5 / (is8Bit ? byte.MaxValue : ushort.MaxValue)) + 1e-7;
        // The samples are compared as they are stored: a grayscale profile does not describe their RGBA copy
        using var typed = image.CloneAs<Rgba64>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        var pixels = new Rgba64[image.Width * image.Height];
        var channels = reference.ChannelCount;
        var worst = 0.0;
        var worstDescription = "";
        for (var frame = 0; frame < reference.FrameCount; frame++)
        {
            typed.Frames[frame].CopyPixelDataTo(pixels);
            for (var y = 0; y < reference.Height; y++)
            {
                for (var x = 0; x < reference.Width; x++)
                {
                    var pixel = pixels[(y * reference.Width) + x];
                    for (var c = 0; c < channels; c++)
                    {
                        // The channels of the file: gray, gray and alpha, RGB or RGBA
                        var actual = (channels, c) switch
                        {
                            (2, 1) or (4, 3) => pixel.A,
                            (_, 0) => pixel.R,
                            (_, 1) => pixel.G,
                            _ => pixel.B,
                        };

                        var expected = Clamp(reference.GetSample(frame, x, y, c));
                        var difference = Math.Abs((actual / 65535.0) - expected);
                        if (difference > worst)
                        {
                            worst = difference;
                            worstDescription = $"frame {frame}, ({x}, {y}), channel {c}: {actual / 65535.0:F5} instead of {expected:F5}";
                        }
                    }
                }
            }
        }

        Assert.True(worst <= allowed, $"{context}: the library differs by {worst:F5} (allowed {allowed:F5}) at {worstDescription}.");
    }

    private static double Clamp(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>Creates an 8-bit image from a pattern, its rows rotated by an offset so that frames differ.</summary>
    private static Image CreateImage(JpegXlPattern pattern, int rowOffset)
    {
        Assert.Equal(255, pattern.MaxValue);
        var rowLength = pattern.Width * pattern.Channels;
        var bytes = new byte[pattern.Samples.Length];
        for (var y = 0; y < pattern.Height; y++)
        {
            var source = (y + rowOffset) % pattern.Height * rowLength;
            for (var i = 0; i < rowLength; i++)
            {
                bytes[(y * rowLength) + i] = (byte)pattern.Samples[source + i];
            }
        }

        return pattern.Channels switch
        {
            1 => Image.ImportPixelBytes<Gray8>(bytes, pattern.Width, pattern.Height),
            3 => Image.ImportPixelBytes<Rgb24>(bytes, pattern.Width, pattern.Height),
            _ => Image.ImportPixelBytes<Rgba32>(bytes, pattern.Width, pattern.Height),
        };
    }
}
