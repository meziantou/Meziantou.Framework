using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Color;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// ---------------------------------------------------------------------------------------------------------------------
// Four-component JPEG fixtures (CMYK and YCCK inks). No pixel format holds inks, so the library only loads them with a
// color conversion: each file is an "unsupported" fixture (identified, rejected by a load without conversion) that also
// records one conversion and its pixels (inkConversion).
//
// Inputs: an RGB JPEG written by cjpeg is separated into inks by LittleCMS jpgicc, which writes a YCCK file with an Adobe
// APP14 segment; jpegtran rewrites its scans without changing a coefficient, and the generator edits the APP14 segment
// to make the CMYK variants (the samples are then read without color transform: other colors, a valid file).
//
// Reference pixels, from independent tools only:
//  1. the samples are decoded by libjpeg-turbo djpeg. No pinned tool writes the four planes of such a file, so jpegtran
//     first codes each component in its own scan (the coefficients are unchanged), and the scans are regrouped, with
//     the same tables and entropy-coded bytes, into files djpeg decodes without color management: a grayscale file
//     for a component, and for YCCK a YCbCr file for the first three, which djpeg converts to RGB;
//  2. the ink amounts follow: stored ink samples are complements (255 is no ink), and the two complements of the YCCK
//     transform cancel out, so the RGB samples djpeg gives for the first three components are their ink amounts;
//  3. the inks are converted by the decimal reference of the test harness (its own profile reader, exact arithmetic).
// Cross-checks: the same inks converted by LittleCMS transicc, and the whole file converted by Apple ImageIO and
// ColorSync through sips. Color management systems interpolate a four-dimensional table differently between its grid
// points, so they are several levels apart: they bound the error of interpretation (ink polarity, channel order, color
// transform, rendering intent) and the tolerance independent implementations justify; the tolerance of each fixture is
// the much smaller disagreement measured for the library.
// ---------------------------------------------------------------------------------------------------------------------
internal static partial class GoldenCorpus
{
    private const string InkSourceProfile = "icc/synthetic-cmyk-lab-lut16";
    private const string InkAssumedProfile = "icc/cgats001-compat-v2-micro";

    // Measured disagreement of the library with each reference (max absolute error, mean absolute color error), printed
    // by Tests/Conformance/JpegDecoderConformanceTests: reviewed measurements, never used as pixel references
    private static readonly Dictionary<string, (int Max, double Mean)> LibraryInkMeasurements = new(StringComparer.Ordinal)
    {
        ["invalid/jpeg/cmyk-adobe-progressive"] = (1, 0.0310),
        ["invalid/jpeg/cmyk-without-adobe-restart"] = (1, 0.0109),
        ["invalid/jpeg/cmyk-ycck-embedded-profile"] = (1, 0.0182),
    };

    /// <summary>One fixture: id, pattern size and seed, whether the CMYK profile is embedded, the Adobe APP14 edit
    /// (null: YCCK as written, 0: transform set to 0, -1: segment removed), jpegtran arguments, the destination
    /// profile, the intent, black point compensation, whether sips can be compared, notes.</summary>
    private sealed record InkCase(string Id, int Width, int Height, int Seed, bool Embedded, int? Adobe, string[] Transcode, string Destination, int Intent, bool BlackPointCompensation,
        bool ColorSync, string Notes);

    private static void BuildInkJpeg(Corpus<GoldenTools> c)
    {
        InkCase[] cases =
        [
            new("invalid/jpeg/cmyk-ycck-embedded-profile", 24, 16, 81, Embedded: true, Adobe: null, [], "icc/srgb-v2-micro", 0, false, ColorSync: true,
                "YCCK inks (Adobe APP14 transform 2) with their CMYK profile in one APP2 segment, baseline, one interleaved scan."),
            new("invalid/jpeg/cmyk-adobe-progressive", 19, 13, 82, Embedded: false, Adobe: 0, ["-progressive"], "icc/srgb-v4", 1, true, ColorSync: false,
                "CMYK inks (Adobe APP14 transform 0) without profile, progressive: the default progression of libjpeg-turbo for four components."),
            new("invalid/jpeg/cmyk-without-adobe-restart", 17, 9, 83, Embedded: false, Adobe: -1, ["-restart", "1B"], "icc/adobe-compat-v2", 0, false, ColorSync: false,
                "Four components without Adobe APP14 segment (JFIF only): CMYK inks stored as complements like every four-component file, without profile, with a restart interval of one MCU."),
        ];

        var t = c.Tools;
        var manifest = CorpusDriver.LoadCommittedManifest();
        foreach (var item in cases)
        {
            var stem = item.Id.Replace('/', '_');
            var img = CommonCorpus.PatternDetailColor(item.Width, item.Height, item.Seed);
            var (_, encodeCommand) = CjpegEncode(c, stem + "-rgb", img, ["-quality", "90", "-sample", "1x1", "-baseline"]);
            var rgbPath = c.Scratch / (stem + "-rgb.jpg");
            List<string> commands = [encodeCommand.Replace("{input}", "{rgb}.jpg", StringComparison.Ordinal)];

            // Separation into inks: jpgicc converts from sRGB (the default input profile) with the perceptual intent
            var separationProfile = ColorProfilePath(manifest, InkSourceProfile);
            var inkPath = c.Scratch / (stem + "-inks.jpg");
            List<string> separate = [t.Jpgicc, "-o" + separationProfile.Value, "-q90"];
            if (item.Embedded)
                separate.Add("-e");
            separate.AddRange([rgbPath.Value, inkPath.Value]);
            Proc.Run(separate);
            commands.Add(CommonCorpus.Display(separate, t, [("-o" + separationProfile.Value, "-o" + ColorProfileFile(manifest, InkSourceProfile)), (rgbPath.Value, "{rgb}.jpg"), (inkPath.Value, "{inks}.jpg")]));

            var path = c.Scratch / (stem + ".jpg");
            List<string> transcode = [t.Jpegtran, "-copy", "all", .. item.Transcode, "-outfile", path.Value, inkPath.Value];
            Proc.Run(transcode);
            commands.Add(CommonCorpus.Display(transcode, t, [(inkPath.Value, "{inks}.jpg"), (path.Value, "{input}")]));
            var data = File.ReadAllBytes(path);

            var adobe = JpegSegments(data).Single(part => part.Kind == 0xEE);
            Py.Assert(Bytes.Slice(data, adobe.Start + 4, adobe.Start + 9).AsSpan().SequenceEqual("Adobe"u8) && data[adobe.End - 1] == 2, item.Id);
            if (item.Adobe == 0)
            {
                data = (byte[])data.Clone();
                data[adobe.End - 1] = 0;
                commands.Add("set the color transform of the Adobe APP14 segment to 0: the four components are CMYK samples");
            }
            else if (item.Adobe == -1)
            {
                data = Bytes.Concat(data[..adobe.Start], data[adobe.End..]);
                commands.Add("remove the Adobe APP14 segment: the four components are CMYK samples");
            }

            File.WriteAllBytes(path, data);
            var (info, features) = CommonCorpus.JpegInspect(data);
            Py.Assert((info.Width, info.Height) == (img.Width, img.Height), item.Id);
            var ycck = item.Adobe is null;

            // 1 and 2. The ink amounts (0 is no ink), from samples decoded by djpeg
            var count = img.Width * img.Height;
            var (inks, planeCommands) = DecodeInks(c, stem, path, img.Width, img.Height, ycck);

            // 3. The conversion of the inks
            var sourceId = item.Embedded ? InkSourceProfile : InkAssumedProfile;
            var sourceProfile = ColorProfilePath(manifest, sourceId);
            var destinationProfile = ColorProfilePath(manifest, item.Destination);
            var reference = ReferenceIccTransform.Create(File.ReadAllBytes(sourceProfile), File.ReadAllBytes(destinationProfile), item.Intent, item.BlackPointCompensation);
            var converted = new byte[count * 3];
            var device = new decimal[4];
            for (var i = 0; i < count; i++)
            {
                for (var channel = 0; channel < 4; channel++)
                    device[channel] = inks[(4 * i) + channel] / 255m;

                var color = reference.Convert(device);
                for (var channel = 0; channel < 3; channel++)
                    converted[(3 * i) + channel] = (byte)decimal.Floor((color[channel] * 255m) + 0.5m);
            }

            var checks = new List<Obj>();

            // Cross-check 1: the same inks converted by LittleCMS, one color at a time (no precalculated device link)
            List<string> transicc = [t.Transicc, "-n", "-c0", "-t" + CommonCorpus.Str(item.Intent)];
            if (item.BlackPointCompensation)
                transicc.Add("-b");
            transicc.AddRange(["-i", sourceProfile.Value, "-o", destinationProfile.Value]);
            var littleCms = ConvertInksWithTransicc(transicc, inks);
            var (maxError, meanError, _) = CommonCorpus.Compare(converted, littleCms, 1, channels: 3, alphaIndex: -1);
            Py.Assert(maxError <= 24 && meanError <= 3, $"{item.Id}: LittleCMS differs by max {maxError}, mean {meanError}");
            checks.Add(new Obj
            {
                ["decoder"] = $"transicc (LittleCMS {t.LittleCmsVersion}) on the inks of the reference",
                ["command"] = CommonCorpus.Display(transicc, t, [(sourceProfile.Value, ColorProfileFile(manifest, sourceId)), (destinationProfile.Value, ColorProfileFile(manifest, item.Destination))]),
                ["result"] = "differs",
                ["maxAbsoluteError"] = maxError,
                ["meanAbsoluteError"] = Py.Round(meanError, 4),
                ["notes"] = "Another color management system on the same inks. The ICC specification does not define the interpolation of a lookup table between its grid points: " +
                            "LittleCMS interpolates a four-dimensional table linearly along its first input and tetrahedrally on the others, the reference multilinearly, " +
                            "so they are several levels apart for some colors: more than the library tolerance of this fixture. The difference bounds an error of table order, " +
                            "encoding or rendering intent, and the tolerance that independent color management systems justify.",
            });

            // Cross-check 2: the whole file converted by Apple ImageIO and ColorSync (perceptual intent), which needs the
            // embedded profile: ImageIO assumes its own CMYK profile for a file without profile
            if (item.ColorSync)
            {
                Py.Assert(item.Embedded && item.Intent == 0 && !item.BlackPointCompensation, item.Id);
                var bmp = c.Scratch / (stem + ".bmp");
                List<string> sipsCommand = [t.Sips, "-m", destinationProfile.Value, "-s", "format", "bmp", path.Value, "--out", bmp.Value];
                Proc.Run(sipsCommand);
                var apple = CommonCorpus.ReadBmp(File.ReadAllBytes(bmp));
                Py.Assert((apple.Width, apple.Height) == (img.Width, img.Height), item.Id);
                var appleRgb = new Img(apple.Width, apple.Height, "rgb", 8, apple.Rgba().Select(pixel => pixel.Slice(0, 3))).Raw("rgb8");
                (maxError, meanError, _) = CommonCorpus.Compare(converted, appleRgb, 1, channels: 3, alphaIndex: -1);
                Py.Assert(maxError <= 24 && meanError <= 3, $"{item.Id}: ColorSync differs by max {maxError}, mean {meanError}");
                checks.Add(new Obj
                {
                    ["decoder"] = $"Apple ImageIO and ColorSync ({t.MacOS}, {t.SipsVersion})",
                    ["command"] = CommonCorpus.Display(sipsCommand, t, [(destinationProfile.Value, ColorProfileFile(manifest, item.Destination)), (path.Value, "{input}"), (bmp.Value, "{output}.bmp")]),
                    ["result"] = "differs",
                    ["maxAbsoluteError"] = maxError,
                    ["meanAbsoluteError"] = Py.Round(meanError, 4),
                    ["notes"] = "An independent decoding and color conversion of the whole file (its own JPEG decoder, ink polarity and YCCK transform, embedded profile, perceptual intent). " +
                                "ColorSync rounds and interpolates differently, so it is a few levels apart: more than the library tolerance of this fixture. The difference bounds an error " +
                                "of interpretation of the file, and the tolerance that independent implementations justify.",
                });
            }

            var inkConversion = new Obj
            {
                ["sourceProfile"] = item.Embedded ? "embedded" : "assumed",
                ["destinationProfile"] = item.Destination,
                ["intent"] = InkIntentNames[item.Intent],
                ["blackPointCompensation"] = item.BlackPointCompensation,
                ["colorModel"] = ycck ? "Ycck" : "Cmyk",
                ["buffer"] = c.Buffer(item.Id, "converted", new Img(img.Width, img.Height, "rgb", 8, Enumerable.Range(0, count).Select(i => Px.From(converted.AsSpan(3 * i, 3)))), "rgb8"),
                ["reference"] = new Obj
                {
                    ["method"] = "decoded",
                    ["description"] = "The samples of each component decoded by libjpeg-turbo djpeg (accurate integer IDCT) from a grayscale JPEG file made of the scan of the component, " +
                                      "after jpegtran coded each component in its own scan without changing a coefficient; ink amounts as the complement of the stored samples" +
                                      (ycck ? " (cyan, magenta and yellow are the RGB samples djpeg decodes from the first three components as a YCbCr file: the complements of the YCCK transform cancel out)" : "") +
                                      $"; inks converted from {sourceId} to {item.Destination} by the decimal reference of the test harness.",
                    ["decoder"] = $"libjpeg-turbo {t.DjpegVersion} djpeg, then ReferenceIccTransform",
                    ["backend"] = $"libjpeg-turbo {t.DjpegVersion} (jidctint islow IDCT{(ycck ? ", jdcolor YCbCr->RGB" : "")}); tests/Meziantou.Framework.Imaging.TestHarness/Color (its own profile reader, decimal arithmetic, multilinear interpolation of four-dimensional tables)",
                    ["command"] = string.Join("; ", planeCommands),
                    ["crossChecks"] = checks,
                },
                ["comparison"] = InkPolicy(item.Id),
            };

            c.AddError(item.Id, "unsupported", item.Id + ".jpg", data, "jpeg",
                c.Provenance("generated", "BuildInkJpeg " + img.PatternName, new Obj { ["width"] = img.Width, ["height"] = img.Height },
                    [t.LibjpegLabel, $"libjpeg-turbo {t.JpegtranVersion} jpegtran", t.LittleCmsLabel, $"Apple ImageIO {t.MacOS} {t.SipsVersion} (cross-check only)", ToolSet.RuntimeLabel], commands),
                new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Jpeg", ["feature"] = "JPEG CMYK/YCCK" },
                features: features,
                notes: item.Notes + " No pixel format holds inks: the file is identified, and loaded only with a color conversion (inkConversion).",
                inkConversion: inkConversion);
        }
    }

    private static readonly string[] InkIntentNames = ["perceptual", "relative-colorimetric", "saturation", "absolute-colorimetric"];

    /// <summary>The library decoder differs from the reference only by the rounding of its inverse DCT, which the color
    /// conversion amplifies.</summary>
    private static Obj InkPolicy(string fixtureId)
    {
        // A fixture without measurement gets a provisional bound: JpegDecoderConformanceTests then reports the measurement
        if (!LibraryInkMeasurements.TryGetValue(fixtureId, out var measured))
            measured = (2, 0.5);

        var (libraryMax, libraryMean) = measured;
        if (libraryMax == 0)
            return new Obj { ["mode"] = "exact" };

        return new Obj
        {
            ["mode"] = "tolerance",
            ["maxAbsoluteError"] = libraryMax,
            ["maxMeanAbsoluteError"] = Math.Ceiling(Py.Round((libraryMean + 0.01) * 100, 6)) / 100,
            ["justification"] =
                "Lossy JPEG compared with an independent decoding of the same encoded input (libjpeg-turbo djpeg: islow integer IDCT), converted by the decimal reference of ICC " +
                "color conversion. The library decoder (double-precision IDCT rounded to nearest) differs from djpeg by one unit on some ink samples, which the color conversion " +
                $"amplifies where its lookup table is steep; its own conversion is exact with respect to the reference. Measured: max {CommonCorpus.Str(libraryMax)}, mean {Py.FormatFixed(libraryMean, 4)}. " +
                "Allowed: the measured max and the measured mean + 0.01 (rounded up to 0.01). The policy is verified to reject channel swaps and flips of this reference.",
        };
    }

    /// <summary>Gets the ink amounts of a four-component file from samples decoded by djpeg. jpegtran codes each
    /// component in its own scan (a lossless transcoding: sequential, the same coefficients, the scans in component
    /// order); the scans are then regrouped into files of the same size (every component has the sampling factors 1x1):
    /// one grayscale file per component, or for YCCK a three-component file, which djpeg reads as YCbCr and converts to
    /// RGB, plus a grayscale file for the fourth component.</summary>
    private static (byte[] Inks, List<string> Commands) DecodeInks(Corpus<GoldenTools> c, string stem, FullPath path, int width, int height, bool ycck)
    {
        var t = c.Tools;
        var script = c.Scratch / (stem + ".separate.txt");
        File.WriteAllText(script, "0;\n1;\n2;\n3;\n");
        var separated = c.Scratch / (stem + ".separate.jpg");
        List<string> transcode = [t.Jpegtran, "-copy", "none", "-scans", script.Value, "-outfile", separated.Value, path.Value];
        Proc.Run(transcode);
        var commands = new List<string>
        {
            CommonCorpus.Display(transcode, t, [(script.Value, "{scans} (0; 1; 2; 3;)"), (separated.Value, "{separated}.jpg"), (path.Value, "{input}")]),
            ycck
                ? "write two JPEG files from {separated}.jpg, each with the quantization tables, a frame header listing its components (sampling factors 1x1) and, for each of its scans, the Huffman tables and the restart interval defined before it: {components}.jpg with the first three components (no APP segment: YCbCr), {component}.jpg with the fourth alone"
                : "write one grayscale JPEG file per component from {separated}.jpg ({component}.jpg): the quantization tables, a frame header with the component alone (sampling factors 1x1), the Huffman tables and the restart interval defined before its scan, the scan",
        };

        var data = File.ReadAllBytes(separated);
        var parts = JpegSegments(data);
        var frame = parts.Single(part => part.Kind is 0xC0 or 0xC1);
        var frameBody = data[(frame.Start + 4)..frame.End];
        Py.Assert(frameBody[5] == 4, stem);
        var components = Enumerable.Range(0, 4).Select(i => (Id: frameBody[6 + (3 * i)], Sampling: frameBody[7 + (3 * i)], Table: frameBody[8 + (3 * i)])).ToList();
        Py.Assert(components.All(component => component.Sampling == 0x11), stem);
        var quantization = Bytes.Concat(parts.Where(part => part.Kind == 0xDB).Select(part => data[part.Start..part.End]));
        var scans = Enumerable.Range(0, parts.Count).Where(index => parts[index].Kind == 0xDA).ToList();
        Py.Assert(scans.Count == 4, stem);
        for (var i = 0; i < 4; i++)
        {
            // One component per scan, in component order
            var header = data[(parts[scans[i]].Start + 4)..parts[scans[i]].End];
            Py.Assert(header[0] == 1 && header[1] == components[i].Id && parts[scans[i] + 1].Kind == JpegEcs, stem);
        }

        // The scans of some components, each preceded by the tables and the restart interval in force
        byte[] Extract(int first, int last)
        {
            var output = new List<byte[]> { new byte[] { 0xFF, 0xD8 }, quantization };
            var frameHeader = new ByteBuilder().Bytes(frameBody.AsSpan(0, 5)).U8(last - first + 1);
            for (var i = first; i <= last; i++)
                frameHeader.U8(components[i].Id).U8(0x11).U8(components[i].Table);
            output.Add(JpegSegment(frame.Kind, frameHeader.ToArray()));
            for (var i = first; i <= last; i++)
            {
                var before = parts.Take(scans[i]).ToList();
                output.AddRange(before.Where(part => part.Kind == 0xC4).Select(part => data[part.Start..part.End]));
                if (before.LastOrDefault(part => part.Kind == 0xDD) is { } restart)
                    output.Add(data[restart.Start..restart.End]);
                output.Add(data[parts[scans[i]].Start..parts[scans[i] + 1].End]);
            }

            output.Add([0xFF, 0xD9]);
            return Bytes.Concat(output);
        }

        Img Decode(string name, byte[] file, string symbol, string model)
        {
            var filePath = c.Scratch / (stem + "." + name + ".jpg");
            File.WriteAllBytes(filePath, file);
            List<string> decode = [t.Djpeg, "-dct", "int", "-pnm", filePath.Value];
            var decoded = CommonCorpus.ReadPnm(Proc.Run(decode));
            Py.Assert((decoded.Width, decoded.Height, decoded.Model) == (width, height, model), stem + " " + name);
            var command = CommonCorpus.Display(decode, t, [(filePath.Value, symbol)]);
            if (!commands.Contains(command, StringComparer.Ordinal))
                commands.Add(command);
            return decoded;
        }

        var count = width * height;
        var inks = new byte[count * 4];
        var black = Decode("component-3", Extract(3, 3), "{component}.jpg", "gray").Raw("gray8");
        if (ycck)
        {
            // Adobe Technical Note #5116: the encoder complements the first three stored samples before converting them
            // like RGB, and the stored samples are themselves complements: the decoded RGB samples are the ink amounts
            var amounts = Decode("components-0-2", Extract(0, 2), "{components}.jpg", "rgb").Raw("rgb8");
            for (var i = 0; i < count; i++)
            {
                amounts.AsSpan(3 * i, 3).CopyTo(inks.AsSpan(4 * i));
                inks[(4 * i) + 3] = (byte)(255 - black[i]);
            }
        }
        else
        {
            var planes = Enumerable.Range(0, 3).Select(i => Decode("component-" + CommonCorpus.Str(i), Extract(i, i), "{component}.jpg", "gray").Raw("gray8")).ToList();
            for (var i = 0; i < count; i++)
            {
                inks[4 * i] = (byte)(255 - planes[0][i]);
                inks[(4 * i) + 1] = (byte)(255 - planes[1][i]);
                inks[(4 * i) + 2] = (byte)(255 - planes[2][i]);
                inks[(4 * i) + 3] = (byte)(255 - black[i]);
            }
        }

        return (inks, commands);
    }

    /// <summary>Converts 8-bit inks to 8-bit RGB with transicc: ink percentages on its standard input, one color per line.</summary>
    private static byte[] ConvertInksWithTransicc(List<string> command, byte[] inks)
    {
        var input = new StringBuilder();
        for (var i = 0; i < inks.Length; i++)
        {
            input.Append((inks[i] * 100.0 / 255).ToString("R", CultureInfo.InvariantCulture));
            input.Append((i + 1) % 4 == 0 ? '\n' : ' ');
        }

        var output = Proc.RunText(command, Encoding.ASCII.GetBytes(input.ToString()));
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("LittleCMS", StringComparison.Ordinal) && !line.StartsWith("Copyright", StringComparison.Ordinal))
            .ToList();
        Py.Assert(lines.Count == inks.Length / 4, $"transicc printed {lines.Count} colors for {inks.Length / 4}");
        var result = new byte[lines.Count * 3];
        for (var i = 0; i < lines.Count; i++)
        {
            var values = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Py.Assert(values.Length == 3, lines[i]);
            for (var channel = 0; channel < 3; channel++)
                result[(3 * i) + channel] = RoundSample(double.Parse(values[channel], CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static byte RoundSample(double value) => (byte)Math.Clamp(Math.Floor(value + 0.5), 0, 255);

    private static string ColorProfileFile(Obj manifest, string id)
    {
        var profile = ((List<object?>)manifest["colorProfiles"]!).Cast<Obj>().Single(profile => (string)profile["id"]! == id);
        return (string)((Obj)profile["file"]!)["path"]!;
    }

    /// <summary>The committed file of a color profile (the icc generator owns it), checked against its recorded hash.</summary>
    private static FullPath ColorProfilePath(Obj manifest, string id)
    {
        var profile = ((List<object?>)manifest["colorProfiles"]!).Cast<Obj>().Single(profile => (string)profile["id"]! == id);
        var file = (Obj)profile["file"]!;
        var path = CorpusDriver.CorpusDir / (string)file["path"]!;
        Py.Assert(Bytes.Sha256Hex(File.ReadAllBytes(path)) == (string)file["sha256"]!, id);
        return path;
    }
}
