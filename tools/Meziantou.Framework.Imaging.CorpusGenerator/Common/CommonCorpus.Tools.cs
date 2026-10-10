using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

internal static partial class CommonCorpus
{
    // bilinear chroma interpolation with the decoder-declared (centered) chroma siting is the closest swscale setting to
    // the JPEG/JFIF interpretation; it is irrelevant for lossless RGB(A)/gray outputs
    public static readonly string[] FfmpegDecodeFlags = ["-fps_mode", "passthrough", "-sws_flags", "bilinear+accurate_rnd+full_chroma_int+bitexact", "-f", "rawvideo"];

    /// <summary>Readable command line for the manifest (tool names instead of absolute paths, symbolic file names).
    /// Substitutions apply in the given order.</summary>
    public static string Display(IEnumerable<string> command, ToolSet tools, IEnumerable<(string Path, string Symbol)> substitutions)
    {
        var names = tools.DisplayNames;
        var list = substitutions.ToList();
        var parts = new List<string>();
        foreach (var argument in command)
        {
            var part = names.TryGetValue(argument, out var name) ? name : argument;
            foreach (var (path, symbol) in list)
                part = part.Replace(path, symbol, StringComparison.Ordinal);
            parts.Add(part);
        }

        return string.Join(' ', parts);
    }

    /// <summary>One pass, no auto-rotation, explicit raw format; verifies the byte count is a whole number of frames.</summary>
    public static (List<byte[]> Frames, List<string> Command) FfmpegDecode(ToolSet tools, FullPath path, string pixelFormat, int width, int height, int bytesPerPixel, bool animated)
    {
        var command = tools.FfmpegCmd([.. (animated ? ["-ignore_loop", "1"] : Array.Empty<string>()), "-noautorotate", "-i", path.Value, .. FfmpegDecodeFlags, "-pix_fmt", pixelFormat, "-"]);
        var output = Proc.Run(command);
        var frameLength = width * height * bytesPerPixel;
        if (output.Length == 0 || output.Length % frameLength != 0)
            throw new ToolException($"ffmpeg produced {output.Length} bytes for {path}, not a whole number of {width}x{height} {pixelFormat} frames");
        return (Chunks(output, frameLength), command);
    }

    public static List<string> FfmpegEncodeStill(ToolSet tools, Img img, FullPath path, params IEnumerable<string> extra)
    {
        var (data, format) = img.FfmpegRaw();
        var source = WithSuffix(path, ".source.raw");
        File.WriteAllBytes(source, data);
        var command = tools.FfmpegCmd(["-f", "rawvideo", "-pixel_format", format, "-video_size", $"{img.Width}x{img.Height}", "-i", source.Value, "-frames:v", "1", "-c:v", "png",
            "-pred", "mixed", .. extra, "-pix_fmt", format, path.Value]);
        Proc.Run(command);
        File.Delete(source);
        return command;
    }

    /// <summary>Path.with_suffix: replaces the last extension of the file name (or appends one).</summary>
    public static FullPath WithSuffix(FullPath path, string suffix)
    {
        var name = path.Name;
        var dot = name.LastIndexOf('.', StringComparison.Ordinal);
        var stem = dot > 0 ? name[..dot] : name;
        return path.Parent / (stem + suffix);
    }

    /// <summary>Consecutive slices of <paramref name="length"/> bytes (the last one may be shorter).</summary>
    public static List<byte[]> Chunks(byte[] data, int length)
    {
        var result = new List<byte[]>();
        for (var i = 0; i < data.Length; i += length)
            result.Add(Bytes.Slice(data, i, i + length));
        return result;
    }

    /// <summary>Samples of a raw buffer: bytes, or little-endian 16-bit values.</summary>
    public static int[] Samples(ReadOnlySpan<byte> data, int sampleBytes)
    {
        if (sampleBytes != 2)
        {
            var bytes = new int[data.Length];
            for (var i = 0; i < data.Length; i++)
                bytes[i] = data[i];
            return bytes;
        }

        var result = new int[data.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = Bytes.U16LE(data, 2 * i);
        return result;
    }

    /// <summary>Same definitions as the managed harness (PixelBufferComparer): returns (max absolute error over all samples,
    /// mean absolute error over color samples, maximum alpha error).</summary>
    public static (int Max, double Mean, int MaxAlpha) Compare(byte[] expected, byte[] actual, int sampleBytes, int channels = 4, int alphaIndex = 3)
    {
        Py.Assert(expected.Length == actual.Length, $"({expected.Length}, {actual.Length})");
        int[] e = Samples(expected, sampleBytes), a = Samples(actual, sampleBytes);
        var errors = e.Zip(a, (x, y) => Math.Abs(x - y)).ToList();
        var color = errors.Where((v, i) => i % channels != alphaIndex).ToList();
        var alpha = errors.Where((v, i) => i % channels == alphaIndex).DefaultIfEmpty(0).ToList();
        return (errors.Max(), (double)color.Sum(v => (long)v) / color.Count, alpha.Max());
    }

    /// <summary>True when every difference is in the color of a pixel that is fully transparent in both buffers.</summary>
    public static bool DiffersOnlyInHiddenColors(byte[] expected, byte[] actual, int sampleBytes)
    {
        int[] e = Samples(expected, sampleBytes), a = Samples(actual, sampleBytes);
        for (var i = 0; i < e.Length; i += 4)
        {
            if (!e.AsSpan(i, 4).SequenceEqual(a.AsSpan(i, 4)) && !(e[i + 3] == 0 && a[i + 3] == 0))
                return false;
        }

        return true;
    }

    /// <summary>True when every differing pixel is a translucent OVER result (its expected value is in roundingValues) and
    /// no sample differs by more than one unit: integer truncation instead of the rounding contract.</summary>
    public static bool DiffersOnlyByRounding(byte[] expected, byte[] actual, int sampleBytes, IReadOnlyCollection<byte[]> roundingValues)
    {
        var pixel = 4 * sampleBytes;
        for (var i = 0; i < expected.Length; i += pixel)
        {
            var e = Bytes.Slice(expected, i, i + pixel);
            var a = Bytes.Slice(actual, i, i + pixel);
            if (e.AsSpan().SequenceEqual(a))
                continue;
            if (!roundingValues.Any(value => value.AsSpan().SequenceEqual(e)))
                return false;
            if (Samples(e, sampleBytes).Zip(Samples(a, sampleBytes), (x, y) => Math.Abs(x - y)).Max() > 1)
                return false;
        }

        return true;
    }

    /// <summary>Fills a crossCheck record: exact, equivalent (only hidden colors of fully transparent pixels differ) or differs.</summary>
    public static void CompareFrames(Obj check, IReadOnlyList<byte[]> expectedRaw, IReadOnlyList<byte[]> decoded, int sample, string hiddenReason)
    {
        var worst = 0;
        var meanTotal = 0.0;
        var differing = new List<int>();
        var hiddenOnly = true;
        foreach (var (i, e, a) in expectedRaw.Zip(decoded).Select((pair, i) => (i, pair.First, pair.Second)))
        {
            var (m, mean, _) = Compare(e, a, sample);
            worst = Math.Max(worst, m);
            meanTotal += mean;
            if (m != 0)
            {
                differing.Add(i);
                hiddenOnly = hiddenOnly && DiffersOnlyInHiddenColors(e, a, sample);
            }
        }

        check["maxAbsoluteError"] = worst;
        check["meanAbsoluteError"] = Py.Round(meanTotal / decoded.Count, 4);
        if (worst == 0)
        {
            check["result"] = "exact";
        }
        else if (hiddenOnly)
        {
            check["result"] = "equivalent";
            check["notes"] = $"Frames {string.Join(',', differing)} differ only in the color of fully transparent pixels: {hiddenReason}. All visible pixels and every alpha value agree.";
        }
        else
        {
            check["result"] = "differs";
            check["notes"] = $"Frames {string.Join(',', differing)} differ from the hand-computed reference.";
        }
    }

    public static Obj HandReference(string description, IList<Obj> crossChecks) => new()
    {
        ["method"] = "hand-computed",
        ["description"] = description,
        ["crossChecks"] = crossChecks,
    };

    public static Obj StillExpected(string format, Img img, string pixelFormat, string colorModel, int bits, int orientation = 1) => new()
    {
        ["format"] = format,
        ["width"] = img.Width,
        ["height"] = img.Height,
        ["pixelFormat"] = pixelFormat,
        ["colorModel"] = colorModel,
        ["bitsPerComponent"] = bits,
        ["orientation"] = orientation,
        ["iccProfile"] = "none",
        ["animation"] = null,
    };

    /// <summary>A reduced "num/den" fraction ("0/1" for zero).</summary>
    public static string Normalize(long num, long den)
    {
        if (num == 0)
            return "0/1";
        var divisor = (long)System.Numerics.BigInteger.GreatestCommonDivisor(num, den);
        return $"{Str(Py.FloorDiv(num, divisor))}/{Str(Py.FloorDiv(den, divisor))}";
    }

    /// <summary>The loosest tolerance the independent cross-checks justify (same formula as
    /// ComparisonPolicy.GetMaximumJustifiedTolerance): (max error, mean error, justified max, justified mean).</summary>
    public static (int MaxError, double MeanError, int JustifiedMax, double JustifiedMean) JpegJustifiedTolerance(IEnumerable<Obj> checks)
    {
        var included = checks.Where(c => !(c.TryGetValue("excludedFromTolerance", out var excluded) && excluded is true)).ToList();
        var maxError = included.Max(c => Convert.ToInt32(c["maxAbsoluteError"], CultureInfo.InvariantCulture));
        var meanError = included.Max(c => Convert.ToDouble(c["meanAbsoluteError"], CultureInfo.InvariantCulture));
        return (maxError, meanError, Math.Max(2, maxError + 1), Math.Ceiling(Math.Max(0.5, meanError * 1.5 + 0.1) * 100) / 100);
    }

    public static void WritePnm(FullPath path, Img img)
    {
        var header = Bytes.Ascii($"{(img.Model == "gray" ? "P5" : "P6")}\n{img.Width} {img.Height}\n255\n");
        var payload = img.Model == "gray" ? img.Pixels.Select(p => (byte)p[0]) : img.Pixels.SelectMany(p => p.ToArray().Select(v => (byte)v));
        File.WriteAllBytes(path, [.. header, .. payload]);
    }

    public static Img ReadPnm(byte[] data)
    {
        // Four header tokens, then exactly one whitespace byte: the samples may start with a whitespace value
        var match = PnmHeaderRegex().Match(Bytes.Latin1(data));
        Py.Assert(match.Success);
        var magic = match.Groups["magic"].Value;
        var width = int.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture);
        var height = int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);
        var maximum = int.Parse(match.Groups["maximum"].Value, CultureInfo.InvariantCulture);
        Py.Assert(maximum == 255);
        var payload = data[(match.Index + match.Length)..];
        if (magic == "P5")
        {
            Py.Assert(payload.Length == width * height);
            return new Img(width, height, "gray", 8, payload.Select(v => new Px(v)));
        }

        Py.Assert(magic == "P6" && payload.Length == width * height * 3);
        return new Img(width, height, "rgb", 8, Chunks(payload, 3).Select(p => Px.From(p)));
    }

    /// <summary>Parses the uncompressed 24-bit (BGR) or 32-bit (BGRA, straight alpha) BMP written by sips (rows padded to 4
    /// bytes, top-down or bottom-up).</summary>
    public static Img ReadBmp(byte[] data)
    {
        Py.Assert(Bytes.StartsWith(data, "BM"u8));
        var offset = (int)Bytes.U32LE(data, 10);
        int width = Bytes.I32LE(data, 18), height = Bytes.I32LE(data, 22), bits = Bytes.U16LE(data, 28);
        var compression = Bytes.U32LE(data, 30);
        Py.Assert(bits is 24 or 32 && compression is 0 or 3, $"({bits}, {compression})");
        var channels = bits / 8;
        var topDown = height < 0;
        height = Math.Abs(height);
        var stride = (width * channels + 3) & ~3;
        var pixels = new List<Px>();
        for (var y = 0; y < height; y++)
        {
            var row = offset + (topDown ? y : height - 1 - y) * stride;
            for (var x = 0; x < width; x++)
            {
                var p = data.AsSpan(row + channels * x, channels);
                pixels.Add(channels == 3 ? new Px(p[2], p[1], p[0]) : new Px(p[2], p[1], p[0], p[3]));
            }
        }

        return new Img(width, height, channels == 3 ? "rgb" : "rgba", 8, pixels);
    }

    /// <summary>Undoes the display transform applied by viewers that honor EXIF orientation (only 1, 3 and 6 are needed).</summary>
    public static Img Unrotate(Img img, int? orientation)
    {
        if (orientation is null or 1)
            return img;
        if (orientation == 3)
        {
            // rotated 180 degrees: displayed(x, y) = stored(W - 1 - x, H - 1 - y)
            return new Img(img.Width, img.Height, img.Model, img.Depth, Enumerable.Reverse(img.Pixels));
        }

        Py.Assert(orientation == 6); // displayed(x', y') = stored(y', H - 1 - x'), displayed width = stored height
        int storedW = img.Height, storedH = img.Width;
        var pixels = Grid(storedW, storedH, (x, y) => img.Pixels[x * img.Width + (storedH - 1 - y)]);
        return new Img(storedW, storedH, img.Model, img.Depth, pixels);
    }

    [GeneratedRegex(@"^(?<magic>P[56])[ \t\n\r\f\v]+(?<width>[0-9]+)[ \t\n\r\f\v]+(?<height>[0-9]+)[ \t\n\r\f\v]+(?<maximum>[0-9]+)[ \t\n\r\f\v]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex PnmHeaderRegex();
}
