using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Dpx;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the DPX and Kodak Cineon fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/dpx, cin,
/// invalid/dpx, invalid/cin and limit/dpx entries). This generator is the reviewed, reproducible recipe of every committed
/// DPX and Cineon fixture. It is NEVER run by the tests and does not use the library under test.
/// <para>
/// Most files are assembled by <see cref="DpxFileBuilder"/> and <see cref="CineonFileBuilder"/> (test harness) from SMPTE
/// ST 268:2014 and the Cineon 4.5 draft: patterns of code values, packed in every way the formats define. Their reference
/// is the pattern (hand-computed): integer code values scaled to the samples of the pixel format, <c>round(v × 65535 /
/// max)</c> for 10 and 12 bits, Cb, Y, Cr converted with the BT.601 or BT.709 matrix. Other files are written by FFmpeg,
/// ImageMagick and OpenImageIO; their reference is the code values the file stores, read back with the rules of the
/// builders.
/// </para>
/// <para>
/// Every valid file is cross-checked with the three decoders of the pinned <c>Meziantou.Prebuilt</c> release, which give
/// the stored code values: FFmpeg in the native layout of the element, OpenImageIO (<c>oiiotool --dumpdata</c>, 10 and
/// 12-bit samples recovered from their 16-bit scaling) and ImageMagick (<c>-set colorspace sRGB</c>, which keeps the code
/// values of logarithmic data). Each fixture declares what each tool gives (exact, a documented difference, a rejection or
/// a feature it does not read); the generation fails when a tool gives something else.
/// </para>
/// <para>
/// Malformed, unsupported and over-limit inputs are header edits of valid builders.
/// </para>
/// It only replaces the DPX and Cineon entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (formats "dpx" and
/// "cin") and their files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dpx
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dpx --write
/// Requirements: FFmpeg, oiiotool and magick from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package. Any OS.
/// </summary>
internal static class DpxCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/DpxCorpus.cs";

    private const string PinnedFfmpeg = "9.0.2";
    private const string PinnedOiio = "3.2.2.0";
    private const string PinnedMagick = "7.1.2-35";

    private static bool IsOwnFixture(Obj fixture) => (string?)fixture["format"] is "dpx" or "cin";

    public static int Run(GeneratorOptions options)
    {
        var tools = new DpxTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-dpx-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} DPX and Cineon fixtures; corpus: {files} files, {bytes} bytes",
            "The committed DPX and Cineon fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, DpxTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<DpxTools>(outDir, tools, ScriptPath))
        {
            BuildDpxLayouts(corpus);
            BuildDpxYCbCr(corpus);
            BuildDpxHeaders(corpus);
            BuildDpxTools(corpus);
            BuildDpxErrors(corpus);
            BuildCineon(corpus);
            BuildCineonErrors(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsOwnFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsOwnFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Patterns and references
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Code values of <paramref name="bits"/> bits that cover the range: 0 and the maximum in the first pixels, then values
    /// whose low bits differ from their high bits, so that a shift, a swap or a truncation is visible.
    /// </summary>
    private static uint[] Codes(int count, int bits, int seed)
    {
        var max = (1u << bits) - 1;
        var codes = new uint[count];
        for (var i = 0; i < count; i++)
        {
            codes[i] = i switch
            {
                0 => 0,
                1 => max,
                _ => (uint)((((ulong)i * 2654435761UL) + ((ulong)seed * 40503UL)) % (max + 1UL)),
            };
        }

        return codes;
    }

    /// <summary>Floating-point values: 0, 1, values above 1, negative values, tiny values and a pattern in between.</summary>
    private static double[] Floats(int count, int seed, bool singlePrecision)
    {
        double[] specials = [0, 1, 2.5, -0.25, 1E-30, 65504, 0.5, 1.0 / 3];
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var value = i < specials.Length ? specials[(i + seed) % specials.Length] : ((i * 37) + seed) % 101 / 64.0;
            values[i] = singlePrecision ? (float)value : value;
        }

        return values;
    }

    /// <summary>The nearest sample of an output depth: <c>(v × outMax + max / 2) / max</c>; 1-bit values are 0 and 255.</summary>
    private static int Scale(uint value, int bits)
    {
        if (bits == 1)
            return value == 0 ? 0 : 255;

        if (bits is 8 or 16)
            return (int)value;

        var max = (1UL << bits) - 1;
        var outMax = bits <= 8 ? 255UL : 65535UL;
        return (int)(((value * outMax) + (max / 2)) / max);
    }

    private static int OutputDepth(int bits) => bits <= 8 ? 8 : 16;

    /// <summary>The expected image of an integer element: the pixels in the order of the file (the orientation is metadata).</summary>
    private static Img ExpectedInteger(int descriptor, int bits, int width, int height, uint[] components, int colorimetric, uint? low, uint? high)
    {
        var depth = OutputDepth(bits);
        var cpp = DpxFileBuilder.GetComponentsPerPixel(descriptor);
        var pixels = new List<Px>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var line = components.AsSpan(y * width * cpp, width * cpp);
                var i = x * cpp;
                pixels.Add(descriptor switch
                {
                    1 or 2 or 3 or 6 => new Px(Scale(line[x], bits)),
                    4 => new Px(0, Scale(line[x], bits)),
                    50 => new Px(Scale(line[i], bits), Scale(line[i + 1], bits), Scale(line[i + 2], bits)),
                    51 => new Px(Scale(line[i], bits), Scale(line[i + 1], bits), Scale(line[i + 2], bits), Scale(line[i + 3], bits)),
                    52 => new Px(Scale(line[i + 3], bits), Scale(line[i + 2], bits), Scale(line[i + 1], bits), Scale(line[i], bits)),
                    _ => YCbCrPixel(descriptor, bits, line, x, width, colorimetric, low, high),
                });
            }
        }

        var model = descriptor switch
        {
            1 or 2 or 3 or 6 => "gray",
            4 => "graya",
            50 or 100 or 102 => "rgb",
            _ => "rgba",
        };
        return new Img(width, height, model, depth, pixels);
    }

    /// <summary>
    /// Converts the Cb, Y, Cr components of a pixel with the BT.709 (colorimetric 5 or 6) or BT.601 matrix, in full range
    /// when the reference codes are 0 and the maximum and in the nominal range otherwise; the chroma of an odd column of a
    /// 4:2:2 element is the mean of its neighbors, rounded up, the last one repeats its left neighbor.
    /// </summary>
    private static Px YCbCrPixel(int descriptor, int bits, ReadOnlySpan<uint> line, int x, int width, int colorimetric, uint? low, uint? high)
    {
        double y, cb, cr;
        uint alpha = 0;
        switch (descriptor)
        {
            case 102:
                (cb, y, cr) = (line[x * 3], line[(x * 3) + 1], line[(x * 3) + 2]);
                break;
            case 103:
                (cb, y, cr, alpha) = (line[x * 4], line[(x * 4) + 1], line[(x * 4) + 2], line[(x * 4) + 3]);
                break;
            default:
            {
                var stride = descriptor == 100 ? 4 : 6;
                var (cbIndex, crIndex) = descriptor == 100 ? (0, 2) : (0, 3);
                var pair = x / 2 * stride;
                y = descriptor == 100 ? line[pair + (x % 2 == 0 ? 1 : 3)] : line[pair + (x % 2 == 0 ? 1 : 4)];
                if (descriptor == 101)
                    alpha = line[pair + (x % 2 == 0 ? 2 : 5)];

                if (x % 2 == 0 || x + 1 >= width)
                {
                    (cb, cr) = (line[pair + cbIndex], line[pair + crIndex]);
                }
                else
                {
                    cb = (line[pair + cbIndex] + line[pair + stride + cbIndex] + 1) / 2;
                    cr = (line[pair + crIndex] + line[pair + stride + crIndex] + 1) / 2;
                }

                break;
            }
        }

        var (kr, kb) = colorimetric is 5 or 6 ? (0.2126, 0.0722) : (0.299, 0.114);
        var max = (1L << bits) - 1;
        double yOffset, yRange, cOffset, cRange;
        if (low == 0 && high == (ulong)max)
        {
            (yOffset, yRange, cOffset, cRange) = (0, max, 1L << (bits - 1), max);
        }
        else
        {
            var scale = Math.Pow(2, bits - 8);
            (yOffset, yRange, cOffset, cRange) = (16 * scale, 219 * scale, 128 * scale, 224 * scale);
        }

        var luma = (y - yOffset) / yRange;
        var pb = (cb - cOffset) / cRange;
        var pr = (cr - cOffset) / cRange;
        var red = luma + (2 * (1 - kr) * pr);
        var blue = luma + (2 * (1 - kb) * pb);
        var green = (luma - (kr * red) - (kb * blue)) / (1 - kr - kb);
        var outMax = bits <= 8 ? 255.0 : 65535.0;
        int Round(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * outMax, MidpointRounding.AwayFromZero);
        return descriptor is 101 or 103
            ? new Px(Round(red), Round(green), Round(blue), Scale(alpha, bits))
            : new Px(Round(red), Round(green), Round(blue));
    }

    private static (string PixelFormat, string ColorModel) PixelFormatOf(int descriptor, int bits)
    {
        if (bits >= 32)
        {
            return descriptor switch
            {
                1 or 2 or 3 or 6 => ("GrayFloat", "Grayscale"),
                4 => ("LaFloat", "GrayscaleAlpha"),
                50 => ("RgbaFloat", "Rgb"),
                _ => ("RgbaFloat", "Rgba"),
            };
        }

        var wide = bits > 8;
        return descriptor switch
        {
            1 or 2 or 3 or 6 => (wide ? "Gray16" : "Gray8", "Grayscale"),
            4 => (wide ? "La32" : "La16", "GrayscaleAlpha"),
            50 => (wide ? "Rgb48" : "Rgb24", "Rgb"),
            51 or 52 => (wide ? "Rgba64" : "Rgba32", "Rgba"),
            100 or 102 => (wide ? "Rgb48" : "Rgb24", "YCbCr"),
            _ => (wide ? "Rgba64" : "Rgba32", "YCbCr"),
        };
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cross-checks
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>What a tool gives for a fixture.</summary>
    private enum Outcome
    {
        Exact,
        Differs,
        Rejects,
    }

    /// <summary>The outcome each tool must give, and why when it is not exact.</summary>
    private sealed record Expectations(Outcome Ffmpeg = Outcome.Exact, Outcome Oiio = Outcome.Exact, Outcome Magick = Outcome.Exact, string? FfmpegNote = null, string? OiioNote = null, string? MagickNote = null)
    {
        public static Expectations AllExact { get; } = new();
    }

    /// <summary>The stored components the file holds, in the order of the descriptor (or of the channels), as integers or as the bits of floats.</summary>
    private sealed record StoredElement(string Format, int Descriptor, int Bits, int Width, int Height, uint[] Components, double[]? Floats);

    private static List<Obj> CrossCheck(Corpus<DpxTools> c, byte[] data, StoredElement element, Expectations expectations)
    {
        var extension = element.Format == "cin" ? ".cin" : ".dpx";
        var path = c.Scratch / ("check" + extension);
        File.WriteAllBytes(path, data);
        var checks = new List<Obj>
        {
            Check(c, "FFmpeg " + c.Tools.FfmpegVersion + " (libavcodec dpx decoder, native layout)", path, element, expectations.Ffmpeg, expectations.FfmpegNote, FfmpegComponents),
            Check(c, "OpenImageIO " + c.Tools.OiioVersion + " (oiiotool --dumpdata)", path, element, expectations.Oiio, expectations.OiioNote, OiioComponents),
            Check(c, "ImageMagick " + c.Tools.MagickVersion + " (-set colorspace sRGB, 16-bit samples)", path, element, expectations.Magick, expectations.MagickNote, MagickComponents),
        };
        return [.. checks.Where(check => check is not null)];
    }

    private static Obj Check(Corpus<DpxTools> c, string decoder, FullPath path, StoredElement element, Outcome expected, string? note, Func<Corpus<DpxTools>, FullPath, StoredElement, (uint[]? Components, string Command)> read)
    {
        uint[]? components;
        string command;
        try
        {
            (components, command) = read(c, path, element);
        }
        catch (ToolException error)
        {
            Expect(expected == Outcome.Rejects, decoder, "Rejects", error.Message);
            return new Obj { ["decoder"] = decoder, ["result"] = FixtureDiffers, ["excludedFromTolerance"] = true, ["notes"] = (note ?? (Discover ? "?" : throw new FatalException(decoder + ": a rejection needs a note"))) + " (" + FirstLine(error.Message) + ")" };
        }

        var expectedComponents = element.Components;
        var differing = components is null ? expectedComponents.Length : components.Zip(expectedComponents).Count(pair => pair.First != pair.Second) + Math.Abs(components.Length - expectedComponents.Length);
        if (differing == 0)
        {
            Expect(expected == Outcome.Exact, decoder, "Exact", "");
            return new Obj
            {
                ["decoder"] = decoder,
                ["command"] = command,
                ["result"] = "exact",
                ["maxAbsoluteError"] = 0,
                ["meanAbsoluteError"] = 0.0,
                ["notes"] = "The tool gives the code values the file stores, component for component.",
            };
        }

        Expect(expected != Outcome.Exact, decoder, "Differs", string.Create(CultureInfo.InvariantCulture, $"{differing} of {expectedComponents.Length}"));
        return new Obj
        {
            ["decoder"] = decoder,
            ["command"] = command,
            ["result"] = FixtureDiffers,
            ["excludedFromTolerance"] = true,
            ["notes"] = string.Create(CultureInfo.InvariantCulture, $"{differing} of the {expectedComponents.Length} code values differ: ") + (note ?? (Discover ? "?" : throw new FatalException(decoder + ": a difference needs a note"))),
        };
    }

    private const string FixtureDiffers = "differs";

    // DPX_CORPUS_DISCOVER=1 lists every tool whose outcome is not the declared one instead of stopping at the first
    private static readonly bool Discover = Environment.GetEnvironmentVariable("DPX_CORPUS_DISCOVER") == "1";

    private static string s_current = "";

    private static string FirstLine(string text) => text.Split('\n')[0].Split(" /", 2)[0].Trim();

    private static void Expect(bool condition, string decoder, string actual, string details)
    {
        if (condition)
            return;

        if (Discover)
        {
            Console.WriteLine($"EXPECT {s_current} | {decoder} | {actual} | {details}");
            return;
        }

        throw new FatalException($"{s_current}: {decoder} gives '{actual}' ({details}): update the expectations of the fixture.");
    }

    /// <summary>The native pixel format FFmpeg decodes an element to, and how to read the code values back.</summary>
    private static (uint[]? Components, string Command) FfmpegComponents(Corpus<DpxTools> c, FullPath path, StoredElement element)
    {
        if (element.Format == "cin")
            throw new ToolException("FFmpeg has no Cineon decoder");

        var (pixelFormat, bytes, planar) = (element.Descriptor, element.Bits) switch
        {
            (50, 8) => ("rgb24", 3, false),
            (51, 8) => ("rgba", 4, false),
            (52, 8) => ("abgr", 4, false),
            (6 or 1 or 2 or 3, 8) => ("gray", 1, false),
            (50, 10) => ("gbrp10le", 6, true),
            (51, 10) => ("gbrap10le", 8, true),
            (6 or 1 or 2 or 3, 10) => ("gray10le", 2, false),
            (50, 12) => ("gbrp12le", 6, true),
            (51, 12) => ("gbrap12le", 8, true),
            (6 or 1 or 2 or 3, 12) => ("gray12le", 2, false),
            (50, 16) => ("rgb48le", 6, false),
            (51, 16) => ("rgba64le", 8, false),
            (6 or 1 or 2 or 3, 16) => ("gray16le", 2, false),
            (50, 32) => ("gbrpf32le", 12, true),
            (51, 32) => ("gbrapf32le", 16, true),
            (102, 8) => ("yuv444p", 3, true),
            (102, 10) => ("yuv444p10le", 6, true),
            (100, 8) => ("uyvy422", 2, false),
            (100, 10) => ("yuv422p10le", 4, true),
            _ => (null, 0, false),
        };
        if (pixelFormat is null)
            throw new ToolException("no native FFmpeg layout");

        var (frames, command) = CommonCorpus.FfmpegDecode(c.Tools, path, pixelFormat, element.Width, element.Height, bytes, animated: false, flags: ["-f", "rawvideo"]);
        var frame = frames[0];
        var display = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]);
        var pixels = element.Width * element.Height;
        var cpp = DpxFileBuilder.GetComponentsPerPixel(element.Descriptor);
        var result = new uint[pixels * cpp];
        if (!planar)
        {
            var size = pixelFormat is "rgb24" or "rgba" or "abgr" or "gray" or "uyvy422" ? 1 : 2;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = size == 1 ? frame[i] : BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(i * 2));
            }

            return (result, display);
        }

        // Planes: G, B, R (and A) for the RGB layouts, Y, U, V for the YUV ones
        var sampleSize = pixelFormat.Contains("f32", StringComparison.Ordinal) ? 4 : pixelFormat is "yuv444p" ? 1 : 2;
        uint Read(int plane, int index, int planeLength) => sampleSize switch
        {
            1 => frame[(plane * planeLength) + index],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(((plane * planeLength) + index) * 2)),
            _ => BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(((plane * planeLength) + index) * 4)),
        };

        if (element.Descriptor is 50 or 51)
        {
            int[] planes = element.Descriptor == 50 ? [2, 0, 1] : [2, 0, 1, 3];
            for (var p = 0; p < pixels; p++)
            {
                for (var k = 0; k < cpp; k++)
                {
                    result[(p * cpp) + k] = Read(planes[k], p, pixels);
                }
            }
        }
        else if (element.Descriptor == 102)
        {
            for (var p = 0; p < pixels; p++)
            {
                result[p * 3] = Read(1, p, pixels);
                result[(p * 3) + 1] = Read(0, p, pixels);
                result[(p * 3) + 2] = Read(2, p, pixels);
            }
        }
        else
        {
            // 4:2:2: a luma plane, then two chroma planes of half the width
            var chroma = pixels / 2;
            for (var p = 0; p < pixels; p += 2)
            {
                var i = p * 2;
                result[i] = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan((pixels + (p / 2)) * 2));
                result[i + 1] = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(p * 2));
                result[i + 2] = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan((pixels + chroma + (p / 2)) * 2));
                result[i + 3] = BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan((p + 1) * 2));
            }
        }

        return (result, display);
    }

    /// <summary>
    /// Reads the pixels with <c>oiiotool --dumpdata</c>: integers of 8 or 16 bits (10 and 12-bit samples scaled to 16 bits by
    /// bit replication, recovered by a shift), or floats. RGB elements are given in R, G, B order; Cb, Y, Cr ones raw
    /// (<c>oiio:RawColor</c>) in the order of the descriptor.
    /// </summary>
    private static (uint[]? Components, string Command) OiioComponents(Corpus<DpxTools> c, FullPath path, StoredElement element)
    {
        var isYCbCr = element.Descriptor is >= 100 and < 150;
        if (isYCbCr && element.Descriptor is 100 or 101)
            throw new ToolException("4:2:2 elements are given as RGB");

        List<string> command = [c.Tools.OiioTool, .. isYCbCr ? new[] { "-iconfig", "oiio:RawColor", "1" } : [], "-v", "--info", "--dumpdata", path.Value];
        var output = System.Text.Encoding.UTF8.GetString(Proc.Run(command));
        var values = new List<double>();
        var pixels = 0;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Pixel (", StringComparison.Ordinal))
                continue;

            pixels++;
            var text = trimmed[(trimmed.IndexOf(':', StringComparison.Ordinal) + 1)..];
            var paren = text.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
                text = text[..paren];

            values.AddRange(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => double.Parse(value, CultureInfo.InvariantCulture)));
        }

        if (pixels != element.Width * element.Height)
            throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"oiiotool printed {pixels} pixels"));

        var display = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]);
        return (ToCodes(values, element, sixteenBitIsReplicated: true), display);
    }

    /// <summary>Reads the samples with ImageMagick as 16-bit integers (or 32-bit floats), the code values kept by <c>-set colorspace sRGB</c>.</summary>
    private static (uint[]? Components, string Command) MagickComponents(Corpus<DpxTools> c, FullPath path, StoredElement element)
    {
        if (element.Descriptor is >= 100 and < 150)
            throw new ToolException("Cb, Y, Cr elements are converted to RGB");

        var map = element.Descriptor switch
        {
            1 or 2 or 3 or 6 => "gray",
            4 => "a",
            50 => "rgb",
            _ => "rgba",
        };
        var isFloat = element.Bits >= 32;
        var output = c.Scratch / "magick.raw";
        List<string> command = [c.Tools.Magick, path.Value, "-set", "colorspace", "sRGB", .. isFloat ? new[] { "-define", "quantum:format=floating-point", "-depth", "32" } : ["-depth", "16"], "-endian", "LSB", map == "a" ? "-alpha" : "-strip", map == "a" ? "extract" : "-strip", (map == "a" ? "gray" : map) + ":" + output.Value];
        Proc.Run(command);
        var data = File.ReadAllBytes(output);
        var values = new List<double>();
        for (var i = 0; i + (isFloat ? 4 : 2) <= data.Length; i += isFloat ? 4 : 2)
        {
            values.Add(isFloat ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(i)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i)));
        }

        var display = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}"), (output.Value, "{output}")]);
        if (element.Descriptor == 52)
        {
            // ImageMagick gives RGBA; the descriptor stores A, B, G, R
            for (var i = 0; i + 3 < values.Count; i += 4)
            {
                (values[i], values[i + 1], values[i + 2], values[i + 3]) = (values[i + 3], values[i + 2], values[i + 1], values[i]);
            }
        }

        return (ToCodes(values, element, sixteenBitIsReplicated: false), display);
    }

    /// <summary>Converts the samples a tool prints to the code values of the element.</summary>
    private static uint[] ToCodes(List<double> values, StoredElement element, bool sixteenBitIsReplicated)
    {
        var bits = element.Bits;
        if (bits >= 32)
            return [.. values.Select(value => BitConverter.SingleToUInt32Bits((float)value))];

        var max = (1L << bits) - 1;
        return [.. values.Select(value =>
        {
            var sample = (long)value;
            if (bits == 1)
                return sample == 0 ? 0u : 1u;

            if (sixteenBitIsReplicated)
                return bits is 10 or 12 ? (uint)(sample >> (16 - bits)) : (uint)sample;

            return (uint)(((sample * max) + 32767) / 65535);
        })];
    }

    // -----------------------------------------------------------------------------------------------------------------
    // DPX fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static DpxFileBuilder Builder(int descriptor, int bits, int packing, int width, int height, bool bigEndian, int seed)
    {
        var builder = new DpxFileBuilder
        {
            Width = width,
            Height = height,
            Descriptor = descriptor,
            BitDepth = bits,
            Packing = packing,
            BigEndian = bigEndian,
            ReferenceLowCode = 0,
            ReferenceHighCode = bits < 32 ? (1u << bits) - 1 : null,
        };
        var count = width * height * builder.ComponentsPerPixel;
        if (bits >= 32)
        {
            builder.FloatComponents = Floats(count, seed, singlePrecision: true);
        }
        else
        {
            builder.Components = Codes(count, bits, seed);
        }

        return builder;
    }

    private static void BuildDpxLayouts(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildDpxLayouts);
        const string OddWidth8 = "OpenImageIO pads no line of 8 or 16-bit components to a 32-bit word (Table 3C requires it), and so misreads the lines of an odd width after the first one.";
        const string OddWidth16 = "OpenImageIO and ImageMagick pad no line of 16-bit components to a 32-bit word (Table 3C requires it, FFmpeg pads them), and so misread the lines of an odd width after the first one.";
        const string Packed = "FFmpeg does not read packed (method 0) 10-bit components.";
        const string OddWidth12 = "OpenImageIO and ImageMagick pad no line of 12-bit components to a 32-bit word (Table 3C requires it, FFmpeg pads them), and so misread the lines after the first one when a line ends in the middle of a word.";
        const string FloatPacking0 = "FFmpeg 9.0.2 rejects these 32-bit floating-point files, as it rejects those OpenImageIO writes.";
        const string OiioFloatText = "oiiotool --dumpdata prints floating-point values with seven significant digits: the values that need more come back rounded.";
        const string MagickFloat = "ImageMagick clamps the negative values of floating-point components and scales the others.";
        const string OneBit = "FFmpeg and OpenImageIO do not read 1-bit components.";
        const string OneBitMagick = "ImageMagick reads (and writes) 1-bit components from the most significant bit of each word, where Table 3B (note 1) puts the first pixel in the least significant bit.";
        const string Gray10Partial = "OpenImageIO reads the last word of a line of a single-component element, when it holds fewer than three components, from its most significant bits.";

        // 8-bit RGB, RGBA and ABGR, luma, in both byte orders; odd widths whose lines are padded to 32-bit words
        AddDpx(c, "rgb-8-be", Function, Builder(50, 8, 0, 5, 3, true, 1), "RGB, 8-bit components, big-endian, 5 pixels per line (lines padded to 32-bit words).", new(Oiio: Outcome.Differs, OiioNote: OddWidth8));
        AddDpx(c, "rgb-8-le", Function, Builder(50, 8, 0, 4, 3, false, 2), "RGB, 8-bit components, little-endian.", Expectations.AllExact);
        AddDpx(c, "rgba-8-be", Function, Builder(51, 8, 0, 4, 3, true, 3), "RGBA, 8-bit components.", Expectations.AllExact);
        AddDpx(c, "abgr-8-be", Function, Builder(52, 8, 0, 4, 3, true, 4), "ABGR, 8-bit components: the components of a pixel are alpha, blue, green and red.",
            new(Oiio: Outcome.Differs, OiioNote: "OpenImageIO gives the components of an ABGR element in the order of the file, not as RGBA.", Magick: Outcome.Differs, MagickNote: "ImageMagick reads the components of an ABGR element in another order."));
        AddDpx(c, "luma-8-be", Function, Builder(6, 8, 0, 8, 3, true, 5), "Luma (Y), 8-bit components.", Expectations.AllExact);
        AddDpx(c, "red-8-be", Function, Builder(1, 8, 0, 8, 2, true, 6), "A red (R) component alone: decoded as gray levels.",
            new(Magick: Outcome.Rejects, MagickNote: "ImageMagick does not read the red single-component descriptor."));

        // 10-bit: filled method A and B, packed; multi-component elements put the first component of a word at the top
        AddDpx(c, "rgb-10-filled-a-be", Function, Builder(50, 10, 1, 6, 3, true, 7), "RGB, 10-bit components filled to 32-bit words with method A: R, G, B from the most significant bits, two padding bits at the bottom.", Expectations.AllExact);
        AddDpx(c, "rgb-10-filled-a-le", Function, Builder(50, 10, 1, 6, 3, false, 8), "RGB, 10-bit components, method A, little-endian words.", Expectations.AllExact);
        AddDpx(c, "rgb-10-filled-b-be", Function, Builder(50, 10, 2, 6, 3, true, 9), "RGB, 10-bit components filled with method B: two padding bits at the top.",
            new(Magick: Outcome.Differs, MagickNote: "ImageMagick reads method B like method A."));
        AddDpx(c, "rgb-10-packed-le", Function, Builder(50, 10, 0, 7, 2, false, 10), "RGB, 10-bit components packed into 32-bit words from the least significant bit, components spanning two words.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: Packed));
        AddDpx(c, "rgba-10-filled-a-be", Function, Builder(51, 10, 1, 6, 2, true, 11), "RGBA, 10-bit components, method A: the four components of a pixel span the words.",
            new(Magick: Outcome.Differs, MagickNote: "ImageMagick reads the 10-bit components of an RGBA element, whose pixels span the words, in another order."));
        AddDpx(c, "luma-10-filled-a-be", Function, Builder(6, 10, 1, 9, 2, true, 12), "Luma, 10-bit components, method A: the first component of a word in its least significant bits (figure C.3).", Expectations.AllExact);
        AddDpx(c, "luma-10-filled-a-partial-word", Function, Builder(6, 10, 1, 5, 2, true, 13), "Luma, 10-bit components, method A, 5 pixels per line: the last word of a line holds two components.",
            new(Oiio: Outcome.Differs, OiioNote: Gray10Partial));
        AddDpx(c, "luma-10-packed-le", Function, Builder(6, 10, 0, 7, 2, false, 14), "Luma, 10-bit components, packed.", new(Ffmpeg: Outcome.Rejects, FfmpegNote: Packed, Magick: Outcome.Differs, MagickNote: "ImageMagick does not read packed 10-bit luma components as a bit stream from the least significant bit."));

        // 12-bit: filled to 16-bit words, packed
        AddDpx(c, "rgb-12-filled-a-be", Function, Builder(50, 12, 1, 5, 3, true, 15), "RGB, 12-bit components filled to 16-bit words with method A (four padding bits at the bottom), 5 pixels per line (lines padded to 32-bit words).",
            new(Oiio: Outcome.Differs, OiioNote: OddWidth12, Magick: Outcome.Differs, MagickNote: OddWidth12));
        AddDpx(c, "rgb-12-filled-b-le", Function, Builder(50, 12, 2, 4, 3, false, 16), "RGB, 12-bit components filled with method B (four padding bits at the top), little-endian.",
            new(Magick: Outcome.Differs, MagickNote: "ImageMagick reads method B like method A."));
        AddDpx(c, "rgb-12-packed-be", Function, Builder(50, 12, 0, 5, 2, true, 17), "RGB, 12-bit components packed into 32-bit words from the least significant bit.",
            Expectations.AllExact);
        AddDpx(c, "luma-12-filled-a-le", Function, Builder(6, 12, 1, 6, 2, false, 18), "Luma, 12-bit components, method A.", Expectations.AllExact);

        // 16-bit
        AddDpx(c, "rgb-16-be", Function, Builder(50, 16, 0, 5, 3, true, 19), "RGB, 16-bit components, 5 pixels per line (lines padded to 32-bit words).",
            new(Oiio: Outcome.Differs, OiioNote: OddWidth16, Magick: Outcome.Differs, MagickNote: OddWidth16));
        AddDpx(c, "rgb-16-le", Function, Builder(50, 16, 0, 4, 3, false, 20), "RGB, 16-bit components, little-endian.", Expectations.AllExact);
        AddDpx(c, "rgba-16-be", Function, Builder(51, 16, 0, 4, 2, true, 21), "RGBA, 16-bit components.", Expectations.AllExact);
        AddDpx(c, "luma-16-be", Function, Builder(6, 16, 0, 6, 2, true, 22), "Luma, 16-bit components.", Expectations.AllExact);

        // Alpha alone: an alpha mask
        AddDpx(c, "alpha-8-be", Function, Builder(4, 8, 0, 8, 2, true, 23), "An alpha component alone (a matte): decoded as black with alpha.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg reads only the luma single-component descriptor.", Oiio: Outcome.Exact, Magick: Outcome.Rejects, MagickNote: "ImageMagick reads only the luma single-component descriptor."));
        AddDpx(c, "alpha-16-le", Function, Builder(4, 16, 0, 6, 2, false, 24), "An alpha component alone, 16 bits: black with alpha.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg reads only the luma single-component descriptor.", Oiio: Outcome.Exact, Magick: Outcome.Rejects, MagickNote: "ImageMagick reads only the luma single-component descriptor."));

        // 1-bit components, from the least significant bit
        AddDpx(c, "luma-1-be", Function, Builder(6, 1, 0, 37, 2, true, 25), "Luma, 1-bit components: a bit stream from the least significant bit of each 32-bit word (Table 3B, note 1), lines of 37 pixels padded to two words.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: OneBit, Oiio: Outcome.Rejects, OiioNote: OneBit, Magick: Outcome.Differs, MagickNote: OneBitMagick));

        // Floating point
        AddDpx(c, "rgb-float32-be", Function, Builder(50, 32, 0, 4, 2, true, 26), "RGB, 32-bit floating-point components: values above 1, negative and tiny values are kept.",
            new(Oiio: Outcome.Differs, OiioNote: OiioFloatText, Ffmpeg: Outcome.Rejects, FfmpegNote: FloatPacking0, Magick: Outcome.Differs, MagickNote: MagickFloat));
        AddDpx(c, "rgba-float32-le", Function, Builder(51, 32, 0, 3, 2, false, 27), "RGBA, 32-bit floating-point components, little-endian.",
            new(Oiio: Outcome.Differs, OiioNote: OiioFloatText, Ffmpeg: Outcome.Rejects, FfmpegNote: FloatPacking0, Magick: Outcome.Differs, MagickNote: MagickFloat));
        AddDpx(c, "luma-float32-be", Function, Builder(6, 32, 0, 4, 2, true, 28), "Luma, 32-bit floating-point components: decoded as gray RGBA floats.",
            new(Oiio: Outcome.Differs, OiioNote: OiioFloatText, Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg does not read floating-point luma.", Magick: Outcome.Differs, MagickNote: MagickFloat));
        var doubles = Builder(50, 64, 0, 4, 2, true, 29);
        AddDpx(c, "rgb-float64-be", Function, doubles, "RGB, 64-bit floating-point components whose values single precision holds exactly: decoded without loss.",
            new(Oiio: Outcome.Differs, OiioNote: OiioFloatText, Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg does not read 64-bit components.", Magick: Outcome.Rejects, MagickNote: "ImageMagick does not read 64-bit components."));

        // Orientation: metadata, the pixels stay in the order of the file
        for (var code = 1; code <= 7; code++)
        {
            var builder = Builder(50, 8, 0, code >= 4 ? 4 : 6, code >= 4 ? 6 : 4, true, 30 + code);
            builder.Orientation = code;
            AddDpx(c, "orientation-" + code.ToString(CultureInfo.InvariantCulture), Function, builder,
                $"The orientation code {code} (Table 2): reported as the orientation of the stored pixels, which keep the order of the file.",
                code <= 3 ? new(Oiio: Outcome.Differs, OiioNote: "OpenImageIO gives the lines and the pixels of the orientation codes 1 to 3 in the standard order, where the library keeps the order of the file and reports the orientation.") : Expectations.AllExact, features: ["dpx.orientation=" + code.ToString(CultureInfo.InvariantCulture)]);
        }
    }

    private static void BuildDpxYCbCr(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildDpxYCbCr);
        const string RawYuv = "The tool converts Cb, Y, Cr components to RGB itself: the stored code values are not compared.";
        const string FfmpegYuv = "FFmpeg 9.0.2 does not read 10-bit Cb, Y, Cr elements (\"not yet implemented\").";
        DpxFileBuilder YCbCr(int descriptor, int bits, int packing, int width, int height, int seed, int colorimetric, bool fullRange)
        {
            var builder = Builder(descriptor, bits, packing, width, height, true, seed);
            builder.Colorimetric = colorimetric;
            builder.Transfer = colorimetric;
            var max = (1u << bits) - 1;
            (builder.ReferenceLowCode, builder.ReferenceHighCode) = fullRange ? (0u, max) : (16u << (bits - 8), 235u << (bits - 8));

            // Values inside the nominal range, so that the conversion does not clip every color
            var codes = builder.Components.ToArray();
            for (var i = 0; i < codes.Length; i++)
            {
                codes[i] = (uint)((16u << (bits - 8)) + (codes[i] % (209u << (bits - 8))));
            }

            builder.Components = codes;
            return builder;
        }

        AddDpx(c, "cbycr-444-10-be", Function, YCbCr(102, 10, 1, 6, 2, 40, 6, fullRange: false), "Cb, Y, Cr 4:4:4, 10-bit components, BT.709 (colorimetric 6), nominal range: converted to RGB.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: FfmpegYuv, Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.ycbcr.matrix=bt709", "dpx.ycbcr.range=limited"]);
        AddDpx(c, "cbycr-444-8-full-601", Function, YCbCr(102, 8, 0, 4, 2, 41, 7, fullRange: true), "Cb, Y, Cr 4:4:4, 8-bit components, BT.601 (colorimetric 7), full range (reference codes 0 and 255).",
            new(Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.ycbcr.matrix=bt601", "dpx.ycbcr.range=full"]);
        AddDpx(c, "cbycry-422-10-be", Function, YCbCr(100, 10, 1, 6, 2, 42, 6, fullRange: false), "Cb, Y, Cr, Y 4:2:2, 10-bit components: the chroma of an odd column is the mean of its neighbors.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: FfmpegYuv, Oiio: Outcome.Rejects, OiioNote: RawYuv, Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.ycbcr.subsampling=422"]);
        AddDpx(c, "cbycry-422-8-be", Function, YCbCr(100, 8, 0, 8, 2, 43, 4, fullRange: false), "Cb, Y, Cr, Y 4:2:2, 8-bit components, unspecified video colorimetry (the BT.601 matrix).",
            new(Oiio: Outcome.Rejects, OiioNote: RawYuv, Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.ycbcr.subsampling=422"]);
        AddDpx(c, "cbyacrya-4224-16-le", Function, YCbCr(101, 16, 0, 4, 2, 44, 6, fullRange: false), "Cb, Y, A, Cr, Y, A 4:2:2:4, 16-bit components.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg does not read 4:2:2:4 elements.", Oiio: Outcome.Rejects, OiioNote: RawYuv, Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.ycbcr.subsampling=422", "dpx.alpha"]);
        AddDpx(c, "cbycra-4444-12-be", Function, YCbCr(103, 12, 1, 4, 2, 45, 7, fullRange: false), "Cb, Y, Cr, A 4:4:4:4, 12-bit components.",
            new(Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg does not read 4:4:4:4 elements.", Oiio: Outcome.Exact, Magick: Outcome.Rejects, MagickNote: RawYuv), features: ["dpx.alpha"]);
    }

    private const string NoIndustry = "OpenImageIO rejects a file whose image data start right after the generic headers (offset 1,664), as FFmpeg writes them.";

    private static void BuildDpxHeaders(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildDpxHeaders);
        var generic = Builder(50, 10, 1, 6, 2, true, 50);
        generic.DataOffset = 1664;
        generic.Version = "V1.0";
        AddDpx(c, "generic-headers-only", Function, generic, "Version 1.0, the image data right after the generic headers (offset 1,664, as FFmpeg writes it): no industry-specific header.", new(Oiio: Outcome.Rejects, OiioNote: NoIndustry), features: ["dpx.version=1.0", "dpx.industryHeaders=none"]);

        var industry = Builder(50, 10, 1, 6, 2, true, 51);
        industry.Text[(160, 100)] = "Meziantou.Framework.Imaging corpus";
        industry.Text[(260, 200)] = "Project X";
        industry.Text[(460, 200)] = "CC0-1.0";
        industry.Text[(36, 100)] = "frame.0001.dpx";
        industry.Text[(136, 24)] = "2026:11:15:10:20:30Z";
        industry.Text[(1556, 32)] = "Scanner";
        industry.Text[(1664, 2)] = "12";
        industry.Text[(1666, 2)] = "34";
        industry.Text[(1668, 2)] = "05";
        industry.Text[(1670, 6)] = "678901";
        industry.Text[(1676, 4)] = "2345";
        industry.Text[(1680, 32)] = "Academy";
        industry.Text[(1732, 32)] = "keyframe";
        industry.Text[(1764, 100)] = "Scene 1, take 2";
        industry.Words[1712] = 1001;
        industry.Words[1716] = 240;
        industry.Words[1720] = 1;
        industry.Words[1724] = BitConverter.SingleToUInt32Bits(24f);
        industry.Words[1728] = BitConverter.SingleToUInt32Bits(180f);
        industry.Words[1920] = 0x01020304;
        industry.Words[1924] = 0;
        industry.Words[1940] = BitConverter.SingleToUInt32Bits(23.976f);
        industry.Words[1948] = BitConverter.SingleToUInt32Bits(2.2f);
        industry.ByteFields[1928] = 0;
        industry.ByteFields[1929] = 1;
        industry.ByteFields[1930] = 0;
        industry.Transfer = 1;
        industry.Colorimetric = 1;
        industry.ReferenceHighQuantity = 2.048f;
        AddDpx(c, "industry-headers", Function, industry, "The file, film and television headers filled: creator, project, film edge code, frame position, frame rate, time code 01:02:03:04, slate. Printing density (transfer 1): the code values are not converted.",
            Expectations.AllExact, features: ["dpx.transfer=printing-density", "dpx.industryHeaders=filled"]);

        var userData = Builder(50, 8, 0, 4, 2, false, 52);
        userData.DataOffset = 8192;
        userData.EndOfLinePadding = 8;
        AddDpx(c, "user-data-and-padding", Function, userData, "6,144 bytes of user-defined data before the image data (offset 8,192), and 8 bytes of end-of-line padding between the lines.",
            new(Ffmpeg: Outcome.Differs, FfmpegNote: "FFmpeg does not skip the end-of-line padding.", Magick: Outcome.Differs, MagickNote: "ImageMagick does not skip the end-of-line padding."), features: ["dpx.userData", "dpx.endOfLinePadding"]);

        var elements = Builder(50, 8, 0, 4, 2, true, 53);
        elements.ElementCount = 2;
        AddDpx(c, "two-elements", Function, elements, "Two image elements: only the first one is decoded.", new(Magick: Outcome.Rejects, MagickNote: "ImageMagick rejects a second image element whose structure is undefined."), features: ["dpx.elements=2"]);

        var linear = Builder(50, 16, 0, 4, 2, true, 54);
        linear.Transfer = 2;
        linear.Colorimetric = 6;
        AddDpx(c, "transfer-linear", Function, linear, "Linear transfer characteristic (2): labeled linear light.", Expectations.AllExact, features: ["dpx.transfer=linear"], transferLinear: true);

        var bt709 = Builder(50, 10, 1, 6, 2, true, 55);
        bt709.Transfer = 6;
        bt709.Colorimetric = 6;
        AddDpx(c, "transfer-bt709", Function, bt709, "BT.709 transfer characteristic and colorimetric specification (6, 6): labeled with the CICP description 1/1/0.", Expectations.AllExact,
            features: ["dpx.transfer=bt709"], cicp: new Obj { ["colorPrimaries"] = 1, ["transferCharacteristics"] = 1, ["matrixCoefficients"] = 0, ["fullRange"] = true });

        var aspect = Builder(50, 8, 0, 4, 2, true, 56);
        aspect.PixelAspectRatio = (2, 1);
        AddDpx(c, "pixel-aspect-ratio", Function, aspect, "A pixel aspect ratio of 2:1 (field 41).", Expectations.AllExact, features: ["dpx.pixelAspectRatio"], pixelAspectRatio: (2, 1));
    }

    private static void BuildDpxTools(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildDpxTools);
        var pattern = new byte[8 * 4 * 6];
        var codes = Codes(8 * 4 * 3, 16, 60);
        for (var i = 0; i < codes.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pattern.AsSpan(i * 2), (ushort)codes[i]);
        }

        var source = c.Scratch / "pattern.rgb48le";
        File.WriteAllBytes(source, pattern);
        var ppm = c.Scratch / "pattern.ppm";
        var ppmData = new List<byte>();
        ppmData.AddRange(System.Text.Encoding.ASCII.GetBytes("P6\n8 4\n65535\n"));
        foreach (var code in codes)
        {
            ppmData.Add((byte)(code >> 8));
            ppmData.Add((byte)code);
        }

        File.WriteAllBytes(ppm, [.. ppmData]);
        foreach (var (name, pixelFormat) in new[] { ("ffmpeg-rgb24", "rgb24"), ("ffmpeg-gbrp10be", "gbrp10be"), ("ffmpeg-gbrp12le", "gbrp12le"), ("ffmpeg-rgb48be", "rgb48be") })
        {
            var output = c.Scratch / (name + ".dpx");
            var command = c.Tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "rgb48le", "-video_size", "8x4", "-i", source.Value, "-frames:v", "1", "-pix_fmt", pixelFormat, "-c:v", "dpx", "-f", "image2", output.Value]);
            Proc.Run(command);
            AddToolFile(c, name, Function, File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(source.Value, "{pattern}.rgb48le"), (output.Value, "{output}")]), $"Written by the DPX encoder of FFmpeg ({pixelFormat}).", new(Oiio: Outcome.Rejects, OiioNote: NoIndustry));
        }

        foreach (var (name, depth, extra) in new[] { ("magick-10", 10, Array.Empty<string>()), ("magick-12", 12, []), ("magick-16-lsb", 16, ["-endian", "LSB"]) })
        {
            var output = c.Scratch / (name + ".dpx");
            List<string> command = [c.Tools.Magick, ppm.Value, "-depth", depth.ToString(CultureInfo.InvariantCulture), .. extra, output.Value];
            Proc.Run(command);
            AddToolFile(c, name, Function, File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(ppm.Value, "{pattern}.ppm"), (output.Value, "{output}")]), $"Written by the DPX coder of ImageMagick ({depth} bits).");
        }

        foreach (var (name, type, extra) in new[] { ("oiio-10", "uint10", Array.Empty<string>()), ("oiio-12-packed", "uint12", []), ("oiio-16-be", "uint16", ["--attrib", "oiio:Endian", "big"]), ("oiio-float", "float", []) })
        {
            var output = c.Scratch / (name + ".dpx");
            List<string> command = [c.Tools.OiioTool, ppm.Value, "-d", type, .. extra, "-o", output.Value];
            Proc.Run(command);
            AddToolFile(c, name, Function, File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(ppm.Value, "{pattern}.ppm"), (output.Value, "{output}")]), $"Written by the DPX writer of OpenImageIO ({type}).", type == "float" ? new(Ffmpeg: Outcome.Rejects, FfmpegNote: "FFmpeg 9.0.2 rejects the 32-bit floating-point files of OpenImageIO.", Oiio: Outcome.Differs, OiioNote: "oiiotool --dumpdata prints floating-point values with seven significant digits: the values that need more come back rounded.", Magick: Outcome.Differs, MagickNote: "ImageMagick clamps the negative values of floating-point components and scales the others.") : null);
        }
    }

    /// <summary>Records a file written by a tool: its reference is what its headers declare, read with the rules of the builder.</summary>
    private static void AddToolFile(Corpus<DpxTools> c, string name, string function, byte[] data, string command, string description, Expectations? expectations = null)
    {
        var bigEndian = data.AsSpan(0, 4).SequenceEqual("SDPX"u8);
        uint U32(int offset) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
        ushort U16(int offset) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        var width = (int)U32(772);
        var height = (int)U32(776);
        var descriptor = data[800];
        var bits = data[803];
        var packing = U16(804);
        var offset = (int)U32(808);
        var builder = new DpxFileBuilder { Width = width, Height = height, Descriptor = descriptor, BitDepth = bits, Packing = packing, BigEndian = bigEndian };
        var cpp = builder.ComponentsPerPixel;
        var lineLength = bits >= 32 ? width * cpp * (bits / 8) : DpxFileBuilder.PackLine(new uint[width * cpp], bits, packing, DpxFileBuilder.IsMultiComponent(descriptor), bigEndian).Length;
        var components = new List<uint>();
        var floats = new List<double>();
        for (var y = 0; y < height; y++)
        {
            var line = data.AsSpan(offset + (y * lineLength), lineLength);
            if (bits >= 32)
            {
                for (var i = 0; i < width * cpp; i++)
                {
                    floats.Add(bigEndian ? BinaryPrimitives.ReadSingleBigEndian(line[(i * 4)..]) : BinaryPrimitives.ReadSingleLittleEndian(line[(i * 4)..]));
                }
            }
            else
            {
                components.AddRange(UnpackLine(line, width * cpp, bits, packing, DpxFileBuilder.IsMultiComponent(descriptor), bigEndian));
            }
        }

        // The rules of the builder must give the file back
        builder.Components = components;
        builder.FloatComponents = floats;
        var repacked = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            repacked.AddRange(bits >= 32
                ? DpxFileBuilder.PackFloatLine([.. floats.Skip(y * width * cpp).Take(width * cpp)], bits, bigEndian)
                : DpxFileBuilder.PackLine([.. components.Skip(y * width * cpp).Take(width * cpp)], bits, packing, DpxFileBuilder.IsMultiComponent(descriptor), bigEndian));
        }

        Py.Assert(data.AsSpan(offset, repacked.Count).SequenceEqual(repacked.ToArray()), name + ": the packing rules of the builder give the image data back");
        var transfer = data[801];
        var colorimetric = data[802];
        AddDpxData(c, "tool-" + name, function, data, new StoredElement("dpx", descriptor, bits, width, height, bits >= 32 ? [.. floats.Select(value => BitConverter.SingleToUInt32Bits((float)value))] : [.. components], bits >= 32 ? [.. floats] : null),
            colorimetric, referenceLow: U32(784) is var low && low != uint.MaxValue ? low : null, referenceHigh: U32(792) is var high && high != uint.MaxValue ? high : null, orientation: U16(768),
            description: description + " The reference is the code values the file stores.", expectations ?? Expectations.AllExact, [command], features: ["dpx.writer=" + name.Split('-')[0]],
            transferLinear: transfer == 2, cicp: transfer == 2 ? null : Cicp(transfer, colorimetric), pixelAspectRatio: null);
    }

    /// <summary>The CICP description of the television transfers and colorimetries of Tables 5A and 5B (R'G'B' in full range).</summary>
    private static Obj? Cicp(int transfer, int colorimetric)
    {
        (int Primaries, int Transfer)? code = (transfer, colorimetric) switch
        {
            (5 or 6, 5 or 6) => (1, 1),
            (7, 7) => (5, 6),
            (8, 8) => (6, 6),
            _ => null,
        };
        return code is { } value ? new Obj { ["colorPrimaries"] = value.Primaries, ["transferCharacteristics"] = value.Transfer, ["matrixCoefficients"] = 0, ["fullRange"] = true } : null;
    }

    private static uint[] UnpackLine(ReadOnlySpan<byte> line, int count, int bits, int packing, bool multiComponent, bool bigEndian)
    {
        uint Word(ReadOnlySpan<byte> data, int index) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data[(index * 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(data[(index * 4)..]);
        ushort Half(ReadOnlySpan<byte> data, int index) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[(index * 2)..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[(index * 2)..]);
        var result = new uint[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = (bits, packing) switch
            {
                (8, _) => line[i],
                (16, _) => Half(line, i),
                (12, not 0) => packing == 1 ? (uint)(Half(line, i) >> 4) : (uint)(Half(line, i) & 0xFFF),
                (10, not 0) => (Word(line, i / 3) >> (packing == 1 ? (multiComponent ? 22 - (10 * (i % 3)) : 2 + (10 * (i % 3))) : (multiComponent ? 20 - (10 * (i % 3)) : 10 * (i % 3)))) & 0x3FF,
                _ => (uint)(((Word(line, (int)((long)i * bits >> 5)) | ((((long)i * bits & 31) + bits > 32 ? (ulong)Word(line, (int)((long)i * bits >> 5) + 1) : 0UL) << 32)) >> (int)((long)i * bits & 31)) & ((1UL << bits) - 1)),
            };
        }

        return result;
    }

    private static void AddDpx(Corpus<DpxTools> c, string name, string function, DpxFileBuilder builder, string description, Expectations expectations, IList<string>? features = null,
        bool transferLinear = false, Obj? cicp = null, (uint, uint)? pixelAspectRatio = null)
    {
        var data = builder.Build();
        var components = builder.BitDepth >= 32 ? [.. builder.FloatComponents.Select(value => BitConverter.SingleToUInt32Bits((float)value))] : builder.Components.ToArray();
        var element = new StoredElement("dpx", builder.Descriptor, builder.BitDepth, builder.Width, builder.Height, components, builder.BitDepth >= 32 ? [.. builder.FloatComponents] : null);
        var allFeatures = new List<string>(features ?? []);
        allFeatures.Add("dpx.byteOrder=" + (builder.BigEndian ? "big" : "little"));
        AddDpxData(c, name, function, data, element, builder.Colorimetric, builder.ReferenceLowCode, builder.ReferenceHighCode, builder.Orientation, description, expectations, [], allFeatures,
            transferLinear || builder.Transfer == 2, cicp ?? Cicp(builder.Transfer, builder.Colorimetric), pixelAspectRatio, packing: builder.Packing, parameters: new Obj { ["width"] = builder.Width, ["height"] = builder.Height, ["descriptor"] = builder.Descriptor, ["bitDepth"] = builder.BitDepth, ["packing"] = builder.Packing });
    }

    private static void AddDpxData(Corpus<DpxTools> c, string name, string function, byte[] data, StoredElement element, int colorimetric, uint? referenceLow, uint? referenceHigh, int orientation,
        string description, Expectations expectations, List<string> commands, IList<string> features, bool transferLinear, Obj? cicp, (uint, uint)? pixelAspectRatio, int? packing = null, Obj? parameters = null)
    {
        var fixtureId = "dpx/" + name;
        s_current = fixtureId;
        var checks = CrossCheck(c, data, element, expectations);
        var (pixelFormat, colorModel) = PixelFormatOf(element.Descriptor, element.Bits);
        var exifOrientation = orientation switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            3 => 3,
            4 => 5,
            5 => 6,
            6 => 8,
            _ => 7,
        };
        var allFeatures = new List<string>(features)
        {
            "dpx.descriptor=" + element.Descriptor.ToString(CultureInfo.InvariantCulture),
            "dpx.bitDepth=" + element.Bits.ToString(CultureInfo.InvariantCulture),
        };
        if (packing is { } method && element.Bits is 10 or 12)
            allFeatures.Add("dpx.packing=" + method switch { 0 => "packed", 1 => "filled-a", _ => "filled-b" });

        AddFixture(c, fixtureId, "dpx", function, data, element, pixelFormat, colorModel, Math.Min(element.Bits, 32), exifOrientation, colorimetric, referenceLow, referenceHigh,
            description, checks, commands, allFeatures, transferLinear, cicp, pixelAspectRatio, parameters);
    }

    private static void AddFixture(Corpus<DpxTools> c, string fixtureId, string format, string function, byte[] data, StoredElement element, string pixelFormat, string colorModel, int bits, int orientation,
        int colorimetric, uint? low, uint? high, string description, List<Obj> checks, List<string> commands, IList<string> features, bool transferLinear, Obj? cicp, (uint H, uint V)? pixelAspectRatio, Obj? parameters)
    {
        var stem = fixtureId;
        var input = c.Write(fixtureId + "." + format, data);
        input["role"] = "encoded";
        var expected = new Obj
        {
            ["width"] = element.Width,
            ["height"] = element.Height,
            ["pixelFormat"] = pixelFormat,
            ["colorModel"] = colorModel,
            ["bitsPerComponent"] = bits,
            ["orientation"] = orientation,
            ["iccProfile"] = "none",
        };
        if (transferLinear)
            expected["transferFunction"] = "linear";
        if (cicp is not null)
            expected["cicp"] = cicp;
        if (pixelAspectRatio is { } ratio)
            expected["pixelAspectRatio"] = new Obj { ["horizontalSpacing"] = ratio.H, ["verticalSpacing"] = ratio.V };

        expected["animation"] = null;
        expected["resolution"] = null;
        expected["profiles"] = new Obj { ["icc"] = null, ["exif"] = null, ["xmp"] = null };
        expected["text"] = new List<object?>();
        expected["frameCount"] = 1;
        List<object?> buffers;
        if (element.Floats is { } floats)
        {
            buffers = [FloatBuffer(c, stem + ".frame-0.rgbaf32le.raw", element.Width, element.Height, ExpectedFloats(element.Descriptor, floats, element.Width * element.Height))];
        }
        else
        {
            var img = format == "cin"
                ? ExpectedCineon(element)
                : ExpectedInteger(element.Descriptor, element.Bits, element.Width, element.Height, element.Components, colorimetric, low, high);
            var layouts = new List<string> { CommonCorpus.CanonicalLayout(img) };
            var native = (img.Model, img.Depth) switch
            {
                ("gray", 8) => "gray8",
                ("gray", 16) => "gray16le",
                ("rgb", 8) => "rgb8",
                _ => null,
            };
            if (native is not null)
                layouts.Add(native);

            buffers = [.. layouts.Select(layout => (object?)c.Buffer(stem, "frame-0", img, layout))];
        }

        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = buffers } };
        expected["poster"] = null;
        var provenance = c.Provenance(commands.Count > 0 ? "generated" : "hand-authored", function, parameters, [c.Tools.FfmpegLabel + " (cross-check)", $"OpenImageIO {c.Tools.OiioVersion} (cross-check{(commands.Any(command => command.StartsWith("oiiotool", StringComparison.Ordinal)) ? ", writer" : "")})", $"ImageMagick {c.Tools.MagickVersion} (cross-check)", ToolSet.RuntimeLabel], commands);
        var sorted = features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        c.Fixtures.Add(new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = format,
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = sorted,
            ["reference"] = new Obj
            {
                ["method"] = "hand-computed",
                ["description"] = description + " Integer code values v of n bits are the samples round(v × 65535 / (2^n - 1)) of 16-bit pixel formats (8 and 16-bit ones as they are, 1-bit ones 0 and 255); floating-point ones are kept.",
                ["crossChecks"] = checks,
            },
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        });
    }

    private static byte[] ExpectedFloats(int descriptor, double[] floats, int pixels)
    {
        var result = new byte[pixels * 16];
        var cpp = DpxFileBuilder.GetComponentsPerPixel(descriptor);
        for (var p = 0; p < pixels; p++)
        {
            float[] pixel = descriptor switch
            {
                1 or 2 or 3 or 6 => [(float)floats[p], (float)floats[p], (float)floats[p], 1f],
                4 => [0f, 0f, 0f, (float)floats[p]],
                50 => [(float)floats[p * 3], (float)floats[(p * 3) + 1], (float)floats[(p * 3) + 2], 1f],
                51 => [(float)floats[p * 4], (float)floats[(p * 4) + 1], (float)floats[(p * 4) + 2], (float)floats[(p * 4) + 3]],
                _ => [(float)floats[(p * cpp) + 3], (float)floats[(p * cpp) + 2], (float)floats[(p * cpp) + 1], (float)floats[p * cpp]],
            };
            for (var k = 0; k < 4; k++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan((p * 16) + (k * 4)), pixel[k]);
            }
        }

        return result;
    }

    private static Obj FloatBuffer(Corpus<DpxTools> c, string path, int width, int height, byte[] bytes)
    {
        var entry = c.Write(path, bytes);
        entry["layout"] = "rgbaf32le";
        entry["width"] = width;
        entry["height"] = height;
        entry["rowBytes"] = width * 16;
        entry["byteLength"] = bytes.Length;
        entry["alpha"] = "straight";
        entry["rowOrder"] = "top-down";
        entry["compression"] = "none";
        return entry;
    }

    private static void BuildDpxErrors(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildDpxErrors);
        DpxFileBuilder Valid() => Builder(50, 10, 1, 6, 2, true, 70);
        void Error(string kind, string name, byte[] data, Obj expected, string notes, Obj? decodeOptions = null)
        {
            var id = (kind == "limit" ? "limit/dpx/" : "invalid/dpx/") + name;
            c.AddError(id, kind, id + ".dpx", data, "dpx", c.Provenance("hand-authored", Function, null, [ToolSet.RuntimeLabel]), expected, decodeOptions: decodeOptions, notes: notes);
        }

        Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "Dpx" };
        Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Dpx", ["feature"] = feature };
        byte[] Edit(Action<DpxFileBuilder> edit)
        {
            var builder = Valid();
            edit(builder);
            return builder.Build();
        }

        var valid = Valid().Build();
        Error("invalid", "truncated-header", valid[..1000], Invalid(), "The file ends in its generic headers.");
        Error("invalid", "truncated-data", valid[..^5], Invalid(), "The last line is cut.");
        Error("invalid", "no-element", Edit(builder => builder.ElementCount = 0), Invalid(), "The file declares 0 image elements.");
        Error("invalid", "nine-elements", Edit(builder => builder.ElementCount = 9), Invalid(), "The file declares 9 image elements; a file has 1 to 8.");
        Error("invalid", "zero-width", Edit(builder => builder.Width = 0), Invalid(), "0 pixels per line.");
        Error("invalid", "orientation-8", Edit(builder => builder.Orientation = 8), Invalid(), "The orientation code 8 is reserved.");
        Error("invalid", "bit-depth-7", Patch(valid, 803, 7), Invalid(), "A bit depth of 7 bits: Table 3A defines 1, 8, 10, 12, 16, 32 and 64.");
        Error("invalid", "data-in-generic-header", Edit(builder => builder.ElementDataOffset = 1000), Invalid(), "The image data start at offset 1,000, inside the generic headers.");
        Error("invalid", "data-sign-2", Edit(builder => builder.DataSign = 2), Invalid(), "The data sign is 2; it is 0 or 1.");
        Error("invalid", "cbycry-odd-width", Edit(builder =>
        {
            builder.Descriptor = 100;
            builder.Width = 5;
            builder.Components = Codes(5 * 2 * 2, 10, 71);
        }), Invalid(), "A 4:2:2 element of 5 pixels per line: Table 1 requires an even number.");

        Error("unsupported", "run-length", Edit(builder => builder.DataEncoding = 1), Unsupported("Run-length encoding"), "Run-length encoded data: no reference decoder reads it (FFmpeg, ImageMagick and OpenImageIO reject it), so it is rejected.");
        Error("unsupported", "encrypted", Edit(builder => builder.EncryptionKey = 0x12345678), Unsupported("Encrypted image data"), "An encryption key: the image data are encrypted.");
        Error("unsupported", "signed", Edit(builder => builder.DataSign = 1), Unsupported("Signed components"), "Signed components.");
        Error("unsupported", "descriptor-depth", Edit(builder =>
        {
            builder.Descriptor = 8;
            builder.Components = Codes(6 * 2, 10, 72);
        }), Unsupported("Descriptor 8"), "The depth (Z) descriptor.");
        Error("unsupported", "descriptor-user-defined", Edit(builder =>
        {
            builder.Descriptor = 0;
            builder.Components = Codes(6 * 2, 10, 73);
        }), Unsupported("Descriptor 0"), "The user-defined single-component descriptor.");
        Error("unsupported", "packing-3", Edit(builder => builder.Packing = 3), Unsupported("Packing 3"), "The reserved packing method 3.");
        Error("unsupported", "cbycr-float", Edit(builder =>
        {
            builder.Descriptor = 102;
            builder.BitDepth = 32;
            builder.FloatComponents = Floats(6 * 2 * 3, 74, singlePrecision: true);
        }), Unsupported("Cb, Y, Cr components of 32 bits"), "Floating-point Cb, Y, Cr components.");
        Error("unsupported", "float64-inexact", Edit(builder =>
        {
            builder.BitDepth = 64;
            builder.Packing = 0;
            builder.FloatComponents = [.. Enumerable.Range(0, 6 * 2 * 3).Select(i => i == 5 ? 0.1 : i / 8.0)];
        }), Unsupported("64-bit floating-point components that single precision does not hold"), "A 64-bit component of 0.1, which single precision does not hold: rounding would lose precision.");

        Error("limit", "frame-pixels-over-limit", valid, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" }, "The image has 12 pixels.",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 11 } });
        Error("limit", "width-over-limit", valid, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "Width" }, "The image is 6 pixels wide.",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxWidth"] = 5 } });
    }

    private static byte[] Patch(byte[] data, int offset, byte value)
    {
        var copy = data.ToArray();
        copy[offset] = value;
        return copy;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cineon fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static CineonFileBuilder Cineon(int channels, int bits, byte packing, int width, int height, bool bigEndian, int seed)
    {
        var builder = new CineonFileBuilder
        {
            Width = width,
            Height = height,
            BitsPerSample = bits,
            Packing = packing,
            BigEndian = bigEndian,
            Designators = channels == 1 ? [0] : [1, 2, 3],
        };
        builder.Samples = Codes(width * height * channels, bits, seed);
        return builder;
    }

    private static Img ExpectedCineon(StoredElement element)
    {
        var channels = element.Descriptor;
        var depth = OutputDepth(element.Bits);
        var max = (1UL << element.Bits) - 1;
        var outMax = depth == 8 ? 255UL : 65535UL;
        int Scale8(uint value) => max == outMax ? (int)value : (int)(((value * outMax) + (max / 2)) / max);
        var pixels = new List<Px>();
        for (var p = 0; p < element.Width * element.Height; p++)
        {
            pixels.Add(channels == 1
                ? new Px(Scale8(element.Components[p]))
                : new Px(Scale8(element.Components[p * 3]), Scale8(element.Components[(p * 3) + 1]), Scale8(element.Components[(p * 3) + 2])));
        }

        return new Img(element.Width, element.Height, channels == 1 ? "gray" : "rgb", depth, pixels);
    }

    private static void BuildCineon(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildCineon);
        const string NoFfmpeg = "FFmpeg has no Cineon decoder.";
        var noFfmpeg = new Expectations(Ffmpeg: Outcome.Rejects, FfmpegNote: NoFfmpeg);
        const string Gray = "ImageMagick reads a Cineon file of one channel as three channels.";
        const string MagickRejects = "ImageMagick rejects little-endian Cineon files.";
        const string OiioNoLinePadding = "OpenImageIO does not skip the padding that ends a line of 8-bit samples on a cell boundary, which ImageMagick writes: it misreads the lines after the first one.";

        AddCineon(c, "rgb-10-be", Function, Cineon(3, 10, 5, 6, 3, true, 80), "RGB, 10-bit samples, one pixel per left-justified 32-bit cell (packing 5): R, G, B from the most significant bits, the common Cineon layout.", noFfmpeg);
        AddCineon(c, "rgb-10-le", Function, Cineon(3, 10, 5, 6, 3, false, 81), "RGB, 10-bit samples, packing 5, little-endian cells.", noFfmpeg with { Magick = Outcome.Rejects, MagickNote = MagickRejects });
        AddCineon(c, "rgb-10-right-justified", Function, Cineon(3, 10, 6, 6, 2, true, 82), "RGB, 10-bit samples right-justified in 32-bit cells (packing 6): two padding bits at the top.",
            noFfmpeg with { Magick = Outcome.Differs, MagickNote = "ImageMagick reads right-justified cells as left-justified ones." });
        AddCineon(c, "gray-10-be", Function, Cineon(1, 10, 5, 9, 2, true, 83), "One channel of 10-bit samples: three samples per 32-bit cell (packing 5), left-justified.",
            noFfmpeg with { Magick = Outcome.Differs, MagickNote = Gray });
        AddCineon(c, "rgb-8-be", Function, Cineon(3, 8, 5, 5, 3, true, 84), "RGB, 8-bit samples, 5 pixels per line: a stream of bytes, each line padded to a whole 32-bit cell (\"align on the next cell boundary\"), as ImageMagick writes them.", noFfmpeg with { Oiio = Outcome.Differs, OiioNote = OiioNoLinePadding });
        AddCineon(c, "rgb-16-le", Function, Cineon(3, 16, 5, 4, 2, false, 85), "RGB, 16-bit samples: a stream of 16-bit words.", noFfmpeg with { Magick = Outcome.Rejects, MagickNote = MagickRejects });
        AddCineon(c, "rgb-12-word-cells", Function, Cineon(3, 12, 3, 4, 2, true, 86), "RGB, 12-bit samples, one per left-justified 16-bit cell (packing 3).",
            noFfmpeg with { Oiio = Outcome.Rejects, OiioNote = "OpenImageIO does not read 12-bit Cineon samples." });
        AddCineon(c, "rgb-12-shared-cells", Function, Cineon(3, 12, 0x85, 4, 2, true, 87), "RGB, 12-bit samples, two per left-justified 32-bit cell, shared by consecutive pixels (packing 0x85, the high bit set).",
            noFfmpeg with { Oiio = Outcome.Rejects, OiioNote = "OpenImageIO does not read 12-bit Cineon samples.", Magick = Outcome.Differs, MagickNote = "ImageMagick reads 12-bit samples as 16-bit words whatever the packing." });

        var reversed = Cineon(3, 10, 5, 6, 2, true, 88);
        reversed.Designators = [3, 2, 1];
        AddCineon(c, "channels-bgr", Function, reversed, "Three channels whose designators are blue, green and red printing densities: placed by their designators.",
            noFfmpeg, reorder: true);

        var oriented = Cineon(3, 10, 5, 4, 6, true, 89);
        oriented.Orientation = 4;
        AddCineon(c, "orientation-4", Function, oriented, "The orientation code 4 (top to bottom, left to right): reported as the orientation of the stored pixels.", noFfmpeg, orientation: 5);

        var film = Cineon(3, 10, 5, 4, 2, true, 90);
        film.Text[(32, 100)] = "frame.0001.cin";
        film.Text[(132, 12)] = "2026:11:15";
        film.Text[(144, 12)] = "10:20:30EST";
        film.Text[(1036, 32)] = "ACADEMY";
        film.Text[(1076, 32)] = "KEYFRAME";
        film.Text[(844, 64)] = "Scanner";
        film.ByteFields[1024] = 12;
        film.ByteFields[1025] = 34;
        film.ByteFields[1026] = 5;
        film.Words[1028] = 678901;
        film.Words[1032] = 2345;
        film.Words[1068] = 1001;
        film.Words[1072] = BitConverter.SingleToUInt32Bits(24f);
        AddCineon(c, "film-header", Function, film, "The origination and motion-picture sections filled: file name, date, device, KEYKODE film edge code, format, frame position and rate.", noFfmpeg);

        var magick = c.Scratch / "pattern-cin.ppm";
        var codes = Codes(6 * 3 * 3, 16, 91);
        File.WriteAllBytes(magick, [.. System.Text.Encoding.ASCII.GetBytes("P6\n6 3\n65535\n"), .. codes.SelectMany(code => new[] { (byte)(code >> 8), (byte)code })]);
        foreach (var depth in new[] { 10, 8 })
        {
            var output = c.Scratch / $"magick-{depth}.cin";
            List<string> command = [c.Tools.Magick, magick.Value, "-depth", depth.ToString(CultureInfo.InvariantCulture), output.Value];
            Proc.Run(command);
            var data = File.ReadAllBytes(output);
            var builder = new CineonFileBuilder { Width = 6, Height = 3, BitsPerSample = depth, Packing = data[681], BigEndian = true, Designators = [0, 0, 0] };
            var stored = ReadCineonSamples(data, 6, 3, 3, depth, data[681]);
            builder.Samples = stored;
            AddCineonData(c, "tool-magick-" + depth.ToString(CultureInfo.InvariantCulture), Function, data, new StoredElement("cin", 3, depth, 6, 3, stored, null),
                $"Written by the Cineon coder of ImageMagick ({depth} bits, packing {data[681]}, the designators of black and white for the three channels). The reference is the code values the file stores (ImageMagick converts the colors to printing densities when it writes).",
                depth == 8 ? noFfmpeg with { Oiio = Outcome.Differs, OiioNote = OiioNoLinePadding } : noFfmpeg, [CommonCorpus.Display(command, c.Tools, [(magick.Value, "{pattern}.ppm"), (output.Value, "{output}")])], 1);
        }
    }

    private static uint[] ReadCineonSamples(byte[] data, int width, int height, int channels, int bits, byte packing)
    {
        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var lineLength = CineonFileBuilder.PackLine(new uint[width * channels], channels, bits, packing, bigEndian: true).Length;
        var samples = new List<uint>();
        for (var y = 0; y < height; y++)
        {
            var line = data.AsSpan(offset + (y * lineLength), lineLength);
            for (var i = 0; i < width * channels; i++)
            {
                samples.Add(bits == 8 ? line[i] : (BinaryPrimitives.ReadUInt32BigEndian(line[(i / 3 * 4)..]) >> (22 - (10 * (i % 3)))) & 0x3FF);
            }
        }

        var result = samples.ToArray();
        for (var y = 0; y < height; y++)
        {
            Py.Assert(data.AsSpan(offset + (y * lineLength), lineLength).SequenceEqual(CineonFileBuilder.PackLine(result[(y * width * channels)..((y + 1) * width * channels)], channels, bits, packing, bigEndian: true)), "the packing rules of the builder give the file back");
        }

        return result;
    }

    private static void AddCineon(Corpus<DpxTools> c, string name, string function, CineonFileBuilder builder, string description, Expectations expectations, bool reorder = false, int orientation = 1)
    {
        var data = builder.Build();
        var samples = builder.Samples.ToArray();
        if (reorder)
        {
            // The reference places the channels by their designators (blue, green, red in the file)
            for (var i = 0; i + 2 < samples.Length; i += 3)
            {
                (samples[i], samples[i + 2]) = (samples[i + 2], samples[i]);
            }
        }

        var element = new StoredElement("cin", builder.Designators.Count, builder.BitsPerSample, builder.Width, builder.Height, samples, null);
        AddCineonData(c, name, function, data, element, description, expectations, [], orientation, reorder ? builder.Samples.ToArray() : null,
            new Obj { ["width"] = builder.Width, ["height"] = builder.Height, ["channels"] = builder.Designators.Count, ["bitsPerSample"] = builder.BitsPerSample, ["packing"] = builder.Packing });
    }

    private static void AddCineonData(Corpus<DpxTools> c, string name, string function, byte[] data, StoredElement element, string description, Expectations expectations, List<string> commands, int orientation,
        uint[]? fileOrder = null, Obj? parameters = null)
    {
        var fixtureId = "cin/" + name;
        s_current = fixtureId;

        // The tools give the channels in the order of the file
        var checks = CrossCheck(c, data, element with { Components = fileOrder ?? element.Components, Descriptor = element.Descriptor == 1 ? 6 : 50 }, expectations);
        var features = new List<string>
        {
            "cin.channels=" + element.Descriptor.ToString(CultureInfo.InvariantCulture),
            "cin.bits=" + element.Bits.ToString(CultureInfo.InvariantCulture),
            "cin.packing=" + data[681].ToString(CultureInfo.InvariantCulture),
            "cin.byteOrder=" + (data[0] == 0x80 ? "big" : "little"),
        };
        var gray = element.Descriptor == 1;
        var depth = OutputDepth(element.Bits);
        AddFixture(c, fixtureId, "cin", function, data, element, gray ? (depth == 8 ? "Gray8" : "Gray16") : (depth == 8 ? "Rgb24" : "Rgb48"), gray ? "Grayscale" : "Rgb", element.Bits, orientation,
            0, null, null, description, checks, commands, features, transferLinear: false, cicp: null, pixelAspectRatio: null, parameters);
    }

    private static void BuildCineonErrors(Corpus<DpxTools> c)
    {
        const string Function = nameof(BuildCineonErrors);
        CineonFileBuilder Valid() => Cineon(3, 10, 5, 6, 2, true, 95);
        void Error(string kind, string name, byte[] data, Obj expected, string notes)
        {
            var id = "invalid/cin/" + name;
            c.AddError(id, kind, id + ".cin", data, "cin", c.Provenance("hand-authored", Function, null, [ToolSet.RuntimeLabel]), expected, notes: notes);
        }

        Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "Cineon" };
        Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Cineon", ["feature"] = feature };
        byte[] Edit(Action<CineonFileBuilder> edit)
        {
            var builder = Valid();
            edit(builder);
            return builder.Build();
        }

        var valid = Valid().Build();
        Error("invalid", "truncated-header", valid[..500], Invalid(), "The file ends in its generic section.");
        Error("invalid", "truncated-data", valid[..^3], Invalid(), "The last line is cut.");
        Error("invalid", "no-channel", Edit(builder => builder.ChannelCount = 0), Invalid(), "The file declares 0 channels.");
        Error("invalid", "orientation-9", Edit(builder => builder.Orientation = 9), Invalid(), "The orientation code 9 is not defined.");
        Error("invalid", "cell-smaller-than-sample", Patch(valid, 681, 1), Invalid(), "10-bit samples in 8-bit cells (packing 1).");

        Error("unsupported", "two-channels", Edit(builder =>
        {
            builder.Designators = [0, 0];
            builder.Samples = Codes(6 * 2 * 2, 10, 96);
        }), Unsupported("Images of 2 channels"), "Two channels.");
        Error("unsupported", "line-interleave", Edit(builder => builder.Interleave = 1), Unsupported("Data interleave 1"), "Line interleave.");
        Error("unsupported", "signed", Edit(builder => builder.Sign = 1), Unsupported("Signed samples"), "Signed samples.");
        Error("unsupported", "channel-sizes", Edit(builder => builder.OtherChannelWidth = 3), Unsupported("Channels of different sizes or depths"), "Channels of different sizes.");
        Error("unsupported", "bit-fields", Patch(valid, 681, 0), Unsupported("Packing 0"), "10-bit samples packed in bit fields (packing 0).");
        Error("unsupported", "bits-32", Patch(Patch(Patch(valid, 198, 32), 226, 32), 254, 32), Unsupported("Samples of more than 16 bits"), "32-bit samples.");
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class DpxTools : ToolSet
    {
        public DpxTools(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            OiioTool = PrebuiltTools.OiioTool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Magick = PrebuiltTools.Magick.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            OiioVersion = PrebuiltTools.OiioTool.Version;
            MagickVersion = PrebuiltTools.Magick.Version;
            if ((FfmpegVersion != PinnedFfmpeg || !OiioVersion.StartsWith(PinnedOiio, StringComparison.Ordinal) || !MagickVersion.StartsWith(PinnedMagick, StringComparison.Ordinal)) && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: FFmpeg {FfmpegVersion} (pinned {PinnedFfmpeg}), OpenImageIO {OiioVersion} (pinned {PinnedOiio}), ImageMagick {MagickVersion} (pinned {PinnedMagick}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [OiioTool] = "oiiotool", [Magick] = "magick" };
        }

        public string OiioTool { get; }

        public string Magick { get; }

        public string OiioVersion { get; }

        public string MagickVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }
    }
}
