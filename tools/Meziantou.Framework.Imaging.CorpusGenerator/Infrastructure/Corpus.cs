using Meziantou.Framework.Imaging.CorpusGenerator.Common;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>Fixture assembly: writes inputs and raw references and records manifest entries.</summary>
internal sealed class Corpus<TTools> : IDisposable
    where TTools : ToolSet
{
    public const string License = "CC0-1.0";
    public const string Author = "Meziantou.Framework.Imaging contributors";

    public Corpus(FullPath outDir, TTools tools, string scriptPath)
    {
        Out = outDir;
        Tools = tools;
        ScriptPath = scriptPath;
        Scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-corpus-scratch-").FullName);
    }

    public FullPath Out { get; }

    public TTools Tools { get; }

    /// <summary>The generator recorded in the provenance of every fixture (committed source file path).</summary>
    public string ScriptPath { get; }

    public List<Obj> Fixtures { get; } = [];

    public FullPath Scratch { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Scratch, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public Obj Write(string relative, byte[] data)
    {
        var path = Out / relative;
        path.CreateParentDirectory();
        File.WriteAllBytes(path, data);
        return new Obj { ["path"] = relative, ["sha256"] = Bytes.Sha256Hex(data) };
    }

    public Obj Buffer(string stem, string role, Img img, string layout)
    {
        var data = img.Raw(layout);
        var entry = Write($"{stem}.{role}.{layout}.raw", data);
        var bpp = layout switch { "rgba8" => 4, "rgba16le" => 8, "rgb8" => 3, "gray8" => 1, "gray16le" => 2, _ => throw new ArgumentOutOfRangeException(nameof(layout)) };
        entry["layout"] = layout;
        entry["width"] = img.Width;
        entry["height"] = img.Height;
        entry["rowBytes"] = img.Width * bpp;
        entry["byteLength"] = img.Width * img.Height * bpp;
        entry["alpha"] = layout.StartsWith("rgba", StringComparison.Ordinal) ? "straight" : "none";
        entry["rowOrder"] = "top-down";
        entry["compression"] = "none";
        return entry;
    }

    public Obj FrameEntry(string stem, string role, Img img, string duration, IReadOnlyList<string> layouts, string? encoding = null, object? encodedDelay = null)
    {
        var frame = new Obj { ["duration"] = duration };
        if (encodedDelay is not null)
            frame["encodedDelay"] = encodedDelay;
        if (!string.IsNullOrEmpty(encoding))
            frame["encoding"] = encoding;
        frame["buffers"] = layouts.Select(layout => (object?)Buffer(stem, role, img, layout)).ToList();
        return frame;
    }

    public void AddValid(string fixtureId, string inputName, byte[] data, IReadOnlyList<Img> frames, Obj expected, Obj provenance, Obj reference,
        IList<string> features, Obj? comparison = null, IReadOnlyList<string>? durations = null, Img? poster = null, IReadOnlyList<string>? layouts = null,
        IReadOnlyList<string?>? frameEncodings = null, string? notes = null, IReadOnlyList<object?>? encodedDelays = null)
    {
        var stem = inputName[..inputName.LastIndexOf('.', StringComparison.Ordinal)];
        var inputEntry = Write(inputName, data);
        inputEntry["role"] = "encoded";
        layouts = layouts is { Count: > 0 } ? layouts : [CommonCorpus.CanonicalLayout(frames[0])];
        durations = durations is { Count: > 0 } ? durations : [.. Enumerable.Repeat("0/1", frames.Count)];
        frameEncodings = frameEncodings is { Count: > 0 } ? frameEncodings : [.. Enumerable.Repeat<string?>(null, frames.Count)];
        encodedDelays = encodedDelays is { Count: > 0 } ? encodedDelays : [.. Enumerable.Repeat<object?>(null, frames.Count)];
        Py.Assert(encodedDelays.Count == frames.Count);
        expected = new Obj(expected);
        foreach (var (key, value) in (IEnumerable<KeyValuePair<string, object?>>)CommonCorpus.MetadataExpectation((string)expected["format"]!, data, Convert.ToInt32(expected["orientation"], System.Globalization.CultureInfo.InvariantCulture), (string)expected["iccProfile"]!))
            expected[key] = value;
        expected["frameCount"] = frames.Count;
        expected["frames"] = frames.Select((f, i) => (object?)FrameEntry(stem, "frame-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), f, durations[i], layouts, frameEncodings[i], encodedDelays[i])).ToList();
        expected["poster"] = poster is not null ? FrameEntry(stem, "poster", poster, "0/1", layouts) : null;
        if (expected["poster"] is Obj posterEntry)
            posterEntry.Remove("duration");
        var format = expected["format"];
        expected.Remove("format");
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = format,
            ["input"] = inputEntry,
            ["provenance"] = provenance,
            ["features"] = features,
            ["reference"] = reference,
            ["expected"] = expected,
            ["comparison"] = comparison ?? new Obj { ["mode"] = "exact" },
        };
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;
        Fixtures.Add(fixture);
    }

    public void AddError(string fixtureId, string kind, string? inputName, byte[]? data, string imageFormat, Obj provenance, Obj expectedError,
        IList<string>? features = null, Obj? decodeOptions = null, string? notes = null, Obj? existingInput = null)
    {
        var inputEntry = existingInput is { Count: > 0 } ? new Obj(existingInput) : Write(inputName!, data!);
        inputEntry["role"] = "encoded";
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = kind,
            ["format"] = imageFormat,
            ["input"] = inputEntry,
            ["provenance"] = provenance,
            ["expectedError"] = expectedError,
        };
        if (features is { Count: > 0 })
            fixture["features"] = features;
        if (decodeOptions is { Count: > 0 })
            fixture["decodeOptions"] = decodeOptions;
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;
        Fixtures.Add(fixture);
    }

    /// <summary>The provenance record. <paramref name="function"/> is the generator method (and its pattern), as recorded
    /// after the script path.</summary>
    public Obj Provenance(string origin, string function, Obj? parameters, IList<string> tools, IList<string>? commands = null)
    {
        var result = new Obj
        {
            ["origin"] = origin,
            ["license"] = License,
            ["author"] = Author,
            ["generator"] = $"{ScriptPath} ({function})",
            ["tools"] = tools,
        };
        if (parameters is not null)
            result["parameters"] = parameters;
        if (commands is { Count: > 0 })
            result["commands"] = commands;
        return result;
    }

    /// <summary>Decodes with FFmpeg (independent of our generator and of the library) and compares with the hand-defined
    /// frames. Returns a crossCheck record; disagreements are recorded, never hidden. With allowFailure, an FFmpeg error (an
    /// unimplemented feature) is recorded as a disagreement instead of aborting the generation.</summary>
    public (Obj Check, List<byte[]>? Decoded) FfmpegCrossCheck(FullPath path, IReadOnlyList<Img> expectedFrames, string layout, bool animated, string description,
        bool posterExpected = false, bool allowFailure = false)
    {
        var first = expectedFrames[0];
        var (pixelFormat, bpp, sample) = layout switch
        {
            "rgba8" => ("rgba", 4, 1),
            "rgba16le" => ("rgba64le", 8, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
        List<byte[]> decoded;
        List<string> command;
        try
        {
            (decoded, command) = CommonCorpus.FfmpegDecode(Tools, path, pixelFormat, first.Width, first.Height, bpp, animated);
        }
        catch (ToolException error) when (allowFailure)
        {
            var reason = error.Message.Split('\n').Where(line => line.Contains("Error during demuxing", StringComparison.Ordinal) || line.Contains("request", StringComparison.OrdinalIgnoreCase) || line.Contains("implemented", StringComparison.Ordinal)).ToList();
            command = Tools.FfmpegCmd([.. (animated ? ["-ignore_loop", "1"] : Array.Empty<string>()), "-noautorotate", "-i", path.Value, .. CommonCorpus.FfmpegDecodeFlags, "-pix_fmt", pixelFormat, "-"]);
            string text;
            if (reason.Count > 0)
            {
                var index = reason[0].IndexOf("] ", StringComparison.Ordinal);
                text = index >= 0 ? reason[0][(index + 2)..] : reason[0];
            }
            else
            {
                text = "error";
            }

            return (new Obj
            {
                ["decoder"] = $"{Tools.FfmpegLabel} (libavcodec {description} decoder)",
                ["command"] = CommonCorpus.Display(command, Tools, [(path.Value, "{input}")]),
                ["result"] = "differs",
                ["notes"] = "FFmpeg failed to decode the input: " + text,
            }, null);
        }

        var check = new Obj
        {
            ["decoder"] = $"{Tools.FfmpegLabel} (libavcodec {description} decoder)",
            ["command"] = CommonCorpus.Display(command, Tools, [(path.Value, "{input}")]),
        };
        var expectedRaw = expectedFrames.Select(f => f.Raw(layout)).ToList();
        if (decoded.Count != expectedRaw.Count)
        {
            check["result"] = "differs";
            check["notes"] = $"FFmpeg produced {decoded.Count} frames, {expectedRaw.Count} expected{(posterExpected ? " (FFmpeg does not expose separate APNG posters)" : "")}.";
            return (check, decoded);
        }

        CommonCorpus.CompareFrames(check, expectedRaw, decoded, sample,
            "FFmpeg fills disposed areas with transparent white (255,255,255,0); the reference uses transparent black");
        return (check, decoded);
    }
}
