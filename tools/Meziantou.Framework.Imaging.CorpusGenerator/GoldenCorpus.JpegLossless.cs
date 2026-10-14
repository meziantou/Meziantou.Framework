using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// ---------------------------------------------------------------------------------------------------------------------
// Lossless JPEG fixtures (ITU-T T.81 Annex H, SOF3). libjpeg-turbo cjpeg writes grayscale and RGB files of any precision
// from 2 to 16 bits: their expected pixels are the pattern given to cjpeg, which djpeg and FFmpeg must both decode
// exactly. FFmpeg's ljpeg encoder writes the YCbCr files, with subsampled chroma in interleaved scans, which libjpeg-turbo
// refuses to convert: their planes are the ones FFmpeg decodes, upsampled and converted by the rules of the other frames.
// ---------------------------------------------------------------------------------------------------------------------
internal static partial class GoldenCorpus
{
    /// <summary>One lossless fixture written by cjpeg: id, pattern at the precision of the file, precision, predictor, point
    /// transform, the other cjpeg arguments, notes, and why FFmpeg does not decode the file like djpeg (it must then
    /// disagree: the generator fails the day it agrees).</summary>
    private sealed record LosslessJpegCase(string Id, Img Image, int Precision, int Predictor, int PointTransform, string[] Arguments, string Notes, string? FfmpegDisagreement = null);

    /// <summary>One lossless fixture written by FFmpeg's ljpeg encoder: id, pattern, pixel format, sampling label and
    /// factors of the chroma planes, prediction method of the encoder, notes.</summary>
    private sealed record LosslessYCbCrCase(string Id, Img Image, string PixelFormat, string Sampling, int Horizontal, int Vertical, int Prediction, string Notes);

    private static void BuildLosslessJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;

        // The first sample of this pattern is 0: its difference from the first prediction, 32,768, is the one difference
        // that has a category of its own and no additional bit (T.81 table H.2)
        var fromZero = CommonCorpus.ToPrecision(CommonCorpus.PatternDetailGray(19, 11, 110), 16);
        fromZero.Pixels[0] = new Px(0);
        fromZero.PatternName += " first sample 0";

        LosslessJpegCase[] cases =
        [
            new("jpeg/lossless-gray-predictor1", CommonCorpus.PatternDetailGray(23, 17, 101), 8, 1, 0, [],
                "Grayscale samples predicted from the sample on the left."),
            new("jpeg/lossless-rgb-predictor4-restart-rows", CommonCorpus.PatternDetailColor(24, 16, 102), 8, 4, 0, ["-restart", "2"],
                "RGB samples (Adobe APP14 transform 0) in one interleaved scan, predictor Ra + Rb - Rc, a restart interval of 2 lines: the line after a restart marker is predicted like the first one."),
            new("jpeg/lossless-rgb-predictor7-point-transform", CommonCorpus.PatternDetailColor(19, 13, 103), 8, 7, 2, [],
                "Point transform 2: the samples are coded divided by 4 and the decoder multiplies them back, so the two low bits of every sample are zero. Predictor (Ra + Rb) / 2."),
            new("jpeg/lossless-gray12-predictor2", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailGray(21, 15, 104), 12), 12, 2, 0, ["-restart", "4"],
                "12-bit grayscale samples predicted from the sample above, a restart interval of 4 lines."),
            new("jpeg/lossless-rgb12-predictor3", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailColor(17, 11, 105), 12), 12, 3, 0, [],
                "12-bit RGB samples predicted from the sample above on the left."),
            new("jpeg/lossless-gray16-predictor5", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailGray(21, 15, 106), 16), 16, 5, 0, [],
                "16-bit grayscale samples, predictor Ra + ((Rb - Rc) >> 1): every bit of a 16-bit sample is stored."),
            new("jpeg/lossless-rgb16-predictor6-restart-rows", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailColor(17, 11, 107), 16), 16, 6, 0, ["-restart", "1"],
                "16-bit RGB samples, predictor Rb + ((Ra - Rc) >> 1), a restart marker after every line."),
            new("jpeg/lossless-gray5-predictor1", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailGray(15, 9, 108), 5), 5, 1, 0, [],
                "5-bit samples: a precision below 8 bits is scaled to 16 bits like the ones above 8 bits."),
            new("jpeg/lossless-rgb10-predictor4-point-transform", CommonCorpus.ToPrecision(CommonCorpus.PatternDetailColor(13, 10, 109), 10), 10, 4, 1, ["-restart", "3"],
                "10-bit RGB samples with a point transform of 1 and a restart interval of 3 lines: the first prediction of each interval is 2^(P - Pt - 1)."),
            new("jpeg/lossless-gray16-largest-difference", fromZero, 16, 1, 0, [],
                "The first sample is 0: its difference from the prediction 32,768 is coded as the category 16, which has no additional bit.",
                FfmpegDisagreement: "the libavcodec decoder reads additional bits after the category 16 (the difference 32,768), which has none (T.81 table H.2): every sample after the first one is wrong. It decodes exactly the 16-bit files that do not code this difference."),
        ];

        foreach (var (fixtureId, img, precision, predictor, pointTransform, arguments, notes, ffmpegDisagreement) in cases)
        {
            var stem = fixtureId.Replace('/', '_');
            var path = c.Scratch / (stem + ".jpg");
            var maximum = (1 << precision) - 1;
            var deep = precision != 8;
            var sampleBytes = deep ? 2 : 1;
            var canonical = deep ? "rgba16le" : "rgba8";
            List<string> encodeArguments = [.. precision == 8 ? [] : new[] { "-precision", CommonCorpus.Str(precision) }, "-lossless", pointTransform == 0 ? CommonCorpus.Str(predictor) : $"{CommonCorpus.Str(predictor)},{CommonCorpus.Str(pointTransform)}", .. arguments];
            var (data, command) = CjpegEncode(c, stem, img, encodeArguments, maximum: maximum);
            var (info, features) = CommonCorpus.JpegInspect(data);
            Py.Assert(info.Sof == 0xC3 && info.Precision == precision && (info.Width, info.Height) == (img.Width, img.Height), fixtureId);
            Py.Assert(info.ScanParameters.Count == 1 && info.ScanParameters[0].Start == predictor && info.ScanParameters[0].Low == pointTransform, fixtureId);

            // The expected samples are the pattern: a lossless file stores them, except the bits the point transform drops
            var samples = new Img(img.Width, img.Height, img.Model, img.Depth, img.Pixels.Select(p => p.Select(v => (v >> pointTransform) << pointTransform)));
            var expectedImage = deep ? CommonCorpus.ScaleToSixteenBits(samples, maximum) : samples;
            var expectedRaw = expectedImage.Raw(canonical);
            var checks = new List<Obj>();

            // Cross-check 1: djpeg writes the samples as they are, in a portable anymap whose maximum value is 2^P - 1
            List<string> decode = [t.Djpeg, "-pnm", path.Value];
            var decoded = CommonCorpus.ReadPnm(Proc.Run(decode));
            if (!decoded.Pixels.SequenceEqual(samples.Pixels))
                throw new FatalException($"{fixtureId}: djpeg does not decode the samples given to cjpeg.");

            checks.Add(new Obj
            {
                ["decoder"] = $"libjpeg-turbo {t.DjpegVersion} djpeg",
                ["command"] = CommonCorpus.Display(decode, t, [(path.Value, "{input}")]) + $" (a portable anymap of the samples, maximum value {CommonCorpus.Str(maximum)})",
                ["result"] = "exact",
                ["maxAbsoluteError"] = 0,
                ["meanAbsoluteError"] = 0.0,
            });

            // Cross-check 2: FFmpeg (libavcodec mjpeg decoder, an independent implementation). It gives the samples as they
            // are stored, or shifted to the width of its pixel format
            var gray = img.Model == "gray";
            var pixelFormat = gray ? (precision <= 8 ? "gray" : "gray16le") : precision <= 8 ? "rgb24" : "rgb48le";
            var ffmpegBytes = precision <= 8 ? 1 : 2;
            var ffCommand = t.FfmpegCmd(["-noautorotate", "-i", path.Value, "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"]);
            var output = Proc.Run(ffCommand);
            var stored = samples.Pixels.SelectMany(p => p.ToArray()).ToArray();
            Py.Assert(output.Length == stored.Length * ffmpegBytes, fixtureId);
            var actual = CommonCorpus.Samples(output, ffmpegBytes);
            var shift = (8 * ffmpegBytes) - precision;
            var asStored = actual.SequenceEqual(stored);
            var shifted = shift > 0 && actual.SequenceEqual(stored.Select(v => v << shift));
            var check = new Obj
            {
                ["decoder"] = $"{t.FfmpegLabel} (libavcodec mjpeg decoder, lossless process)",
                ["command"] = CommonCorpus.Display(ffCommand, t, [(path.Value, "{input}")]) +
                              (shifted ? $" (the samples shifted left by {CommonCorpus.Str(shift)} bits)" : " (the samples as they are stored)"),
                ["result"] = "exact",
                ["maxAbsoluteError"] = 0,
                ["meanAbsoluteError"] = 0.0,
            };
            if (ffmpegDisagreement is null)
            {
                if (!asStored && !shifted)
                    throw new FatalException($"{fixtureId}: {t.FfmpegLabel} does not decode the samples given to cjpeg; declare the disagreement and its reason if it is one of the decoder.");
            }
            else
            {
                if (asStored || shifted)
                    throw new FatalException($"{fixtureId}: {t.FfmpegLabel} decodes the file exactly: it is a cross-check now, remove the declared disagreement.");

                // Measured on the 8-bit scale, like every error of the corpus
                Py.Assert(precision is 8 or 16, fixtureId);
                var ffmpegImage = new Img(img.Width, img.Height, img.Model, precision, actual.Chunk(Img.Channels(img.Model)).Select(p => Px.From(p)));
                var (maxError, meanError, _) = JpegCompare(expectedRaw, ffmpegImage.Raw(canonical), sampleBytes);
                check["command"] = CommonCorpus.Display(ffCommand, t, [(path.Value, "{input}")]);
                check["result"] = "differs";
                check["maxAbsoluteError"] = maxError;
                check["meanAbsoluteError"] = Py.Round(meanError, 4);
                check["excludedFromTolerance"] = true;
                check["notes"] = "A defect of the decoder, not a reading of the file: " + ffmpegDisagreement;
            }

            checks.Add(check);

            var colorModel = gray ? "Grayscale" : "Rgb";
            var expected = CommonCorpus.StillExpected("jpeg", img, deep ? (gray ? "Gray16" : "Rgba64") : gray ? "Gray8" : "Rgb24", colorModel, precision);
            var reference = CommonCorpus.HandReference(
                "The pattern given to cjpeg: a lossless file stores its samples" +
                (pointTransform == 0 ? "" : $", without the {CommonCorpus.Str(pointTransform)} low bit(s) the point transform drops (each sample s is (s >> {CommonCorpus.Str(pointTransform)}) << {CommonCorpus.Str(pointTransform)})") +
                (deep && precision != 16 ? $"; each {CommonCorpus.Str(precision)}-bit sample s scaled to 16 bits as (s * 65535 + {CommonCorpus.Str(maximum / 2)}) / {CommonCorpus.Str(maximum)}." : "."),
                checks);
            List<string> layouts = deep ? (gray ? ["rgba16le", "gray16le"] : ["rgba16le"]) : gray ? ["rgba8", "gray8"] : ["rgba8", "rgb8"];
            c.AddValid(fixtureId, fixtureId + ".jpg", data, [expectedImage], expected,
                c.Provenance("generated", "BuildLosslessJpeg " + img.PatternName, new Obj { ["width"] = img.Width, ["height"] = img.Height, ["precision"] = precision },
                    [t.LibjpegLabel, t.FfmpegLabel + " (cross-check only)", ToolSet.RuntimeLabel], [command]),
                reference, features, comparison: new Obj { ["mode"] = "exact" }, layouts: layouts, notes: notes);
        }

        BuildLosslessYCbCrJpeg(c);
        BuildInvalidLosslessJpeg(c);
    }

    /// <summary>YCbCr lossless files written by FFmpeg's ljpeg encoder: three components in one interleaved scan, the
    /// chroma planes subsampled or not. libjpeg-turbo decodes them but refuses the color conversion ("Unsupported
    /// color conversion request": it only keeps what is lossless), so the reference has two stages: the planes FFmpeg decodes
    /// from the file, then the chroma upsampling and the color conversion of the frames of the other processes, which the
    /// generator computes (<see cref="CommonCorpus.JpegUpsample"/>, <see cref="CommonCorpus.JpegYCbCrToRgb"/>) and which the
    /// DCT fixtures compare with djpeg.</summary>
    private static void BuildLosslessYCbCrJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        LosslessYCbCrCase[] cases =
        [
            new("jpeg/lossless-ffmpeg-ycbcr-444", CommonCorpus.PatternDetailColor(24, 16, 121), "yuvj444p", "4:4:4", 1, 1, 1,
                "YCbCr samples without chroma subsampling, predicted from the sample on the left: only the color conversion stands between the samples and the pixels. " +
                "The encoder declares the sampling factors 1x2 for the three components: nothing is subsampled, and an MCU holds two lines of each component."),
            new("jpeg/lossless-ffmpeg-ycbcr-420-odd", CommonCorpus.PatternDetailColor(37, 23, 122), "yuvj420p", "4:2:0", 2, 2, 2,
                "4:2:0 with an odd width and an odd height: an MCU holds 2x2 luma samples and one sample of each chroma plane, and the samples that complete the last MCUs are coded and removed. Predicted from the sample above."),
            new("jpeg/lossless-ffmpeg-ycbcr-422", CommonCorpus.PatternDetailColor(29, 13, 123), "yuvj422p", "4:2:2", 2, 1, 3,
                "4:2:2 with an odd width and an odd height, predicted from the sample above on the left. The encoder declares the sampling factors 2x2 for the luma and 1x2 for the chroma: an MCU holds two lines of each component."),
        ];

        foreach (var (fixtureId, img, pixelFormat, sampling, horizontal, vertical, prediction, notes) in cases)
        {
            var stem = fixtureId.Replace('/', '_');
            var path = c.Scratch / (stem + ".jpg");
            var source = CommonCorpus.WithSuffix(path, ".source.raw");
            File.WriteAllBytes(source, img.FfmpegRaw().Data);
            var size = $"{CommonCorpus.Str(img.Width)}x{CommonCorpus.Str(img.Height)}";
            var encode = t.FfmpegCmd(["-f", "rawvideo", "-pixel_format", "rgb24", "-video_size", size, "-i", source.Value,
                "-frames:v", "1", "-c:v", "ljpeg", "-pred", CommonCorpus.Str(prediction), "-fflags", "+bitexact", "-flags:v", "+bitexact", "-pix_fmt", pixelFormat, path.Value]);
            Proc.Run(encode);
            File.Delete(source);
            var data = File.ReadAllBytes(path);
            var (info, features) = CommonCorpus.JpegInspect(data);
            Py.Assert(info.Sof == 0xC3 && info.Precision == 8 && (info.Width, info.Height) == (img.Width, img.Height), fixtureId);
            // The bit-exact output has no JFIF segment (and no comment that names the encoder): three components that are not
            // named R, G and B are YCbCr
            Py.Assert(info.Components.Select(component => component.Id).SequenceEqual([1, 2, 3]) && info.ScanParameters.Count == 1, fixtureId);
            Py.Assert(!features.Any(feature => feature.StartsWith("jpeg.app", StringComparison.Ordinal)), fixtureId);

            // What matters is the ratio of the factors: the encoder gives every component of its 4:4:4 and 4:2:2 files the
            // vertical factor 2, so that an MCU holds two lines of each
            var maxHorizontal = info.Components.Max(component => component.H);
            var maxVertical = info.Components.Max(component => component.V);
            Py.Assert((info.Components[0].H, info.Components[0].V) == (maxHorizontal, maxVertical), fixtureId);
            Py.Assert(info.Components.Skip(1).All(component => (maxHorizontal / component.H, maxVertical / component.V) == (horizontal, vertical)), fixtureId);

            // Stage 1: the planes, which FFmpeg decodes without converting them
            var decode = t.FfmpegCmd(["-noautorotate", "-i", path.Value, "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"]);
            var planes = Proc.Run(decode);
            var chromaWidth = Py.CeilDiv(img.Width, horizontal);
            var chromaHeight = Py.CeilDiv(img.Height, vertical);
            var lumaLength = img.Width * img.Height;
            var chromaLength = chromaWidth * chromaHeight;
            Py.Assert(planes.Length == lumaLength + (2 * chromaLength), fixtureId);
            var luma = planes.AsSpan(0, lumaLength).ToArray().Select(v => (int)v).ToArray();
            var blue = planes.AsSpan(lumaLength, chromaLength).ToArray().Select(v => (int)v).ToArray();
            var red = planes.AsSpan(lumaLength + chromaLength, chromaLength).ToArray().Select(v => (int)v).ToArray();

            // Stage 2: the chroma samples of every pixel, then its color
            var reference = new Img(img.Width, img.Height, "rgb", 8, CommonCorpus.Grid(img.Width, img.Height, (x, y) =>
            {
                var (r, g, b) = CommonCorpus.JpegYCbCrToRgb(
                    luma[(y * img.Width) + x],
                    CommonCorpus.JpegUpsample(blue, chromaWidth, chromaHeight, horizontal, vertical, x, y),
                    CommonCorpus.JpegUpsample(red, chromaWidth, chromaHeight, horizontal, vertical, x, y));
                return new Px(r, g, b);
            }));
            var referenceRgba = reference.Raw("rgba8");

            // Cross-check: the colors FFmpeg converts its planes to. Without subsampling the only difference is the rounding
            // of the color conversion; with it, swscale interpolates the chroma planes in its own way
            var (converted, convertCommand) = CommonCorpus.FfmpegDecode(t, path, "rgba", img.Width, img.Height, 4, false);
            var (maxError, meanError, alphaError) = JpegCompare(referenceRgba, converted[0], 1);
            Py.Assert(alphaError == 0, fixtureId);
            var subsampled = horizontal != 1 || vertical != 1;
            Py.Assert(subsampled || maxError <= 1, fixtureId);
            var check = new Obj
            {
                ["decoder"] = $"{t.FfmpegLabel} (libavcodec mjpeg decoder, lossless process; swscale yuvj->rgba)",
                ["command"] = CommonCorpus.Display(convertCommand, t, [(path.Value, "{input}")]),
                ["result"] = maxError == 0 ? "exact" : "differs",
                ["maxAbsoluteError"] = maxError,
                ["meanAbsoluteError"] = Py.Round(meanError, 4),
            };
            if (maxError != 0)
            {
                check["excludedFromTolerance"] = true;
                check["notes"] = subsampled
                    ? "The planes are the ones of the reference (the same decoder). swscale interpolates the subsampled chroma planes bilinearly, where the " +
                      "reference applies the triangle filters and the edge replication of libjpeg-turbo: an upsampling difference that the detailed chroma of " +
                      "this pattern makes large, and that does not justify a tolerance for a lossless file."
                    : "The planes are the ones of the reference (the same decoder): the conversion of swscale rounds some colors by one unit differently " +
                      "from the 16-bit fixed-point JFIF conversion of the reference, which does not justify a tolerance for a lossless file.";
            }

            var expected = CommonCorpus.StillExpected("jpeg", img, "Rgb24", "YCbCr", 8);
            var reference2 = new Obj
            {
                ["method"] = "decoded",
                ["description"] = "The Y, Cb and Cr planes decoded from the same encoded input by FFmpeg, which are the samples the file stores, then converted " +
                                  "by the generator like the planes of the frames of the other processes: " +
                                  (subsampled ? "the chroma planes upsampled with the triangle filters, the edge replication and the rounding biases of libjpeg-turbo's fancy upsampling, and " : "") +
                                  "the JFIF full-range YCbCr->RGB conversion in 16-bit fixed point. libjpeg-turbo djpeg decodes the planes but refuses to convert the colors of a lossless file.",
                ["decoder"] = t.FfmpegLabel,
                ["backend"] = "libavcodec mjpeg decoder (lossless process) for the planes; chroma upsampling and YCbCr->RGB conversion computed by the generator (CommonCorpus.JpegUpsample, CommonCorpus.JpegYCbCrToRgb)",
                ["command"] = CommonCorpus.Display(decode, t, [(path.Value, "{input}")]),
                ["crossChecks"] = new List<Obj> { check },
            };
            c.AddValid(fixtureId, fixtureId + ".jpg", data, [reference], expected,
                c.Provenance("generated", "BuildLosslessYCbCrJpeg " + img.PatternName, new Obj { ["width"] = img.Width, ["height"] = img.Height },
                    [t.FfmpegLabel, ToolSet.RuntimeLabel], [CommonCorpus.Display(encode, t, [(source.Value, "{source}.raw"), (path.Value, "{input}")])]),
                reference2, features, comparison: new Obj { ["mode"] = "exact" }, layouts: ["rgba8", "rgb8"], notes: notes);
        }
    }

    /// <summary>Malformed lossless files derived from valid cjpeg outputs (InvalidImageContentException). The scan header
    /// defects are found by a full scan; the defects of the entropy-coded data and the restart interval only by decoding.</summary>
    private static void BuildInvalidLosslessJpeg(Corpus<GoldenTools> c)
    {
        var t = c.Tools;
        var img = CommonCorpus.PatternDetailColor(20, 12, 131);
        var (plain, plainCommand) = CjpegEncode(c, "invalid-lossless-plain", img, ["-lossless", "4"]);
        var (restart, restartCommand) = CjpegEncode(c, "invalid-lossless-restart", img, ["-lossless", "4", "-restart", "1"]);

        void Add(string fixtureId, byte[] data, string baseCommand, string defect, string notes) =>
            c.AddError(fixtureId, "invalid", fixtureId + ".jpg", data, "jpeg",
                c.Provenance("generated", "BuildInvalidLosslessJpeg PatternDetailColor", new Obj { ["width"] = 20, ["height"] = 12, ["defect"] = defect },
                    [t.LibjpegLabel, ToolSet.RuntimeLabel], [baseCommand, defect]),
                new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Jpeg" },
                features: CommonCorpus.JpegInspect(data).Features, notes: notes);

        var parts = JpegSegments(plain);
        var sos = parts.First(p => p.Kind == 0xDA);
        var body = plain[(sos.Start + 4)..sos.End];
        Py.Assert(body[0] == 3 && body[7] == 4 && body[8] == 0 && body[9] == 0);
        body[7] = 0;
        Add("invalid/jpeg/lossless-predictor-0", Bytes.Concat(plain[..sos.Start], JpegSegment(0xDA, body), plain[sos.End..]), plainCommand,
            "set the predictor selection of the scan header (Ss) to 0",
            "The predictor 0 (no prediction) is for the differential frames of the hierarchical mode: a lossless scan selects a predictor from 1 to 7 (T.81 table H.1).");
        body[7] = 4;
        body[9] = 8;
        Add("invalid/jpeg/lossless-point-transform-8", Bytes.Concat(plain[..sos.Start], JpegSegment(0xDA, body), plain[sos.End..]), plainCommand,
            "set the point transform of the scan header (Al) to 8",
            "A point transform of 8 leaves no bit of the 8-bit samples.");

        var ecs = parts.First(p => p.Kind == JpegEcs);
        var middle = ecs.Start + ((ecs.End - ecs.Start) / 2);
        if (plain[middle - 1] == 0xFF)
            middle--;
        Add("invalid/jpeg/lossless-truncated-scan", Bytes.Concat(plain[..middle], plain[ecs.End..]), plainCommand,
            "cut the entropy-coded data of the scan in the middle (EOI kept)",
            "The scan ends before its last sample: never decoded as a partial image.");

        var dri = JpegSegments(restart).First(p => p.Kind == 0xDD);
        Py.Assert(Bytes.U16BE(restart, dri.Start + 4) == 20);
        var partial = (byte[])restart.Clone();
        partial[dri.Start + 5] = 19;
        Add("invalid/jpeg/lossless-restart-interval-partial-line", partial, restartCommand,
            "set the restart interval (DRI) to 19 MCUs, one less than a line",
            "The restart interval of a lossless scan is a whole number of MCU rows (T.81 H.1.1): the prediction of a line that a restart marker cuts is not defined.");
    }
}
