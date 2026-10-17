using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Compares what the library decodes from a JPEG XL file with the floating-point output of the two reference decoders,
/// libjxl's <c>djxl</c> and jxl-oxide, which share no code.
/// </summary>
internal static class JpegXlReferenceComparison
{
    /// <summary>The distance to djxl, as a fraction of the range: libjxl approximates powers, transfer functions and the error function.</summary>
    public const double DjxlTolerance = 0.0008;

    /// <summary>The distance to jxl-oxide, as a fraction of the range.</summary>
    public const double OxideTolerance = 0.0004;

    /// <summary>A reference decoder is not compared: the case says why.</summary>
    public const double Skip = -1;

    /// <summary>Decodes a file with the library and with the reference decoders, and compares every sample of every frame.</summary>
    /// <returns>The image the library decoded.</returns>
    public static async Task<Image> AssertDecodesLikeTheReferencesAsync(FullPath path, double djxlTolerance, double oxideTolerance, string context)
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

    public static void AssertFramesMatch(Image image, NpyFrames reference, double tolerance, string context)
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
}
