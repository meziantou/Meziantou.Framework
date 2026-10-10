using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the color management part of the corpus (the <c>colorProfiles</c> and <c>colorTransforms</c> sections of the
/// manifest, and the icc/ directory): ICC profiles under an allowed license, and colors converted between them by an
/// independent color management system, LittleCMS, through its <c>transicc</c> tool.
/// </summary>
/// <remarks>
/// <para>
/// The profiles come from a checkout of https://github.com/saucecontrol/Compact-ICC-Profiles (CC0-1.0) at the pinned
/// commit, given by --icc-profiles; each file is checked against its pinned SHA-256 before it is copied.
/// </para>
/// <para>
/// <c>transicc</c> runs without precalculation (-c0), so that LittleCMS evaluates its conversion for each color instead
/// of interpolating a device link, and prints each sample with four decimals on a 0-255 scale (0-100 for CMYK). Colors
/// are sent with full 16-bit precision; the printed samples are stored rounded to 16 bits. Two color management systems
/// never agree bit for bit: the tolerance of each conversion was measured against the library under test and is recorded
/// with its justification. The tolerances are deliberately too small to hide an error of encoding, of table order or of
/// rendering intent, which changes samples by whole levels.
/// </para>
/// <para>
/// The colors of the CMYK conversions are the grid points of the color lookup table of the profile (6 per channel): the
/// device values that the input tables of the lut16Type map to multiples of one fifth, found by inverting these
/// tables. The ICC specification does not define the interpolation between grid points and color management systems
/// differ there: on this coarse table, LittleCMS
/// (linear along the first input, tetrahedral on the others), Apple ColorSync and this library (both multilinear) are
/// several levels apart for some colors, while they agree on the grid points. Interpolation is therefore not covered
/// by these vectors; the reference of the test harness covers it.
/// </para>
/// </remarks>
internal static partial class IccCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/IccCorpus.cs";

    private const string ProfilesRepository = "https://github.com/saucecontrol/Compact-ICC-Profiles";
    private const string ProfilesCommit = "bdd84663061bc4ae95ca70decff54f581e27f702";
    private const string PinnedLittleCms = "2.19";

    /// <summary>The largest difference caused by rounding: the samples printed by transicc and their storage on 16 bits.</summary>
    private const string RoundingJustification = "Both systems evaluate the same matrix and tone curves; the difference is the rounding of the four decimals printed by transicc, of the 16-bit vectors, and of the single-precision arithmetic of LittleCMS.";

    /// <summary>Sampled curves: LittleCMS inverts and interpolates them through tables of its own.</summary>
    private const string SampledCurveJustification = "Both systems evaluate the same matrix and tone curves, but LittleCMS evaluates a sampled curve in single precision and inverts it through a 4096-entry table, while the library inverts the piecewise-linear curve exactly; the difference stays below a tenth of a level of 255.";

    /// <summary>Grid points of the color lookup table: no interpolation is involved.</summary>
    private const string GridPointJustification = "The colors are the grid points of the color lookup table, where no interpolation is involved and the two systems must agree; the difference is the rounding of the four decimals printed by transicc, of the 16-bit vectors and of the device values of the grid points.";

    private static readonly Profile[] Profiles =
    [
        new("icc/srgb-v4", "sRGB-v4.icc", "c56e1685d888f5edb92fe07f2750f387f8fe8e91b32ff8fb0b56bfbbb9458353", 3, ["icc.version=4", "icc.model=matrix", "icc.curve=parametric", "icc.space=rgb"]),
        new("icc/srgb-v2-micro", "sRGB-v2-micro.icc", "0a8a33aea66a6f154a5642ebe168ef287e73265d9f7b51c42a45e6eedbacda7a", 3, ["icc.version=2", "icc.model=matrix", "icc.curve=sampled", "icc.space=rgb"]),
        new("icc/display-p3-v4", "DisplayP3-v4.icc", "cb51de38e482ee974c0c76b9689e16aad04bad16e226fed2f30c842d15ff3a3d", 3, ["icc.version=4", "icc.model=matrix", "icc.curve=parametric", "icc.space=rgb"]),
        new("icc/adobe-compat-v2", "AdobeCompat-v2.icc", "60fb2adecacf82132db0b1c09b303316f3bbd9e2823e7ba096d01627d12d57c9", 3, ["icc.version=2", "icc.model=matrix", "icc.curve=gamma", "icc.space=rgb"]),
        new("icc/prophoto-v4", "ProPhoto-v4.icc", "090daf740c136b4a63bf979d64f034b4a65aa5abbb04a0917729222afe2bb5c2", 3, ["icc.version=4", "icc.model=matrix", "icc.curve=parametric", "icc.space=rgb"]),
        new("icc/rec2020-v4", "Rec2020-v4.icc", "135ebd418b668c0ca56a8dea6d262c4deccae8166e0ca8264b7d7f35863ccb4b", 3, ["icc.version=4", "icc.model=matrix", "icc.curve=parametric", "icc.space=rgb"]),
        new("icc/sgrey-v4", "sGrey-v4.icc", "00c0f94e09127520a17dc0e1d9264b5702081d96dbdb1549ee88e3631ce42a9d", 1, ["icc.version=4", "icc.model=monochrome", "icc.curve=parametric", "icc.space=gray"]),
        new("icc/sgrey-v2-nano", "sGrey-v2-nano.icc", "ac2805b9e07b6fa3ba7406aa48e84d87041097da66220cc2a385e17bb2893cc1", 1, ["icc.version=2", "icc.model=monochrome", "icc.curve=sampled", "icc.space=gray"]),
        new("icc/cgats001-compat-v2-micro", "CGATS001Compat-v2-micro.icc", "73e1ba37d2bad5bab2a964f40a9eed96209666efc067c3322626214bbef234a0", 4, ["icc.version=2", "icc.model=lut16", "icc.pcs=lab", "icc.space=cmyk", "icc.class=input"]),
    ];

    private static readonly Transform[] Transforms =
    [
        new("icc/srgb-v4-to-display-p3-v4", "icc/srgb-v4", "icc/display-p3-v4", 1, false, 2, 0.5, RoundingJustification),
        new("icc/display-p3-v4-to-srgb-v4", "icc/display-p3-v4", "icc/srgb-v4", 1, false, 2, 0.5, RoundingJustification),
        new("icc/srgb-v2-micro-to-rec2020-v4", "icc/srgb-v2-micro", "icc/rec2020-v4", 1, false, 4, 0.5, SampledCurveJustification),
        new("icc/adobe-compat-v2-to-srgb-v2-micro", "icc/adobe-compat-v2", "icc/srgb-v2-micro", 1, false, 10, 0.75, SampledCurveJustification),
        new("icc/prophoto-v4-to-adobe-compat-v2", "icc/prophoto-v4", "icc/adobe-compat-v2", 0, false, 2, 0.5, RoundingJustification),
        new("icc/rec2020-v4-to-srgb-v4-absolute", "icc/rec2020-v4", "icc/srgb-v4", 3, false, 2, 0.5, RoundingJustification),
        new("icc/sgrey-v4-to-sgrey-v2-nano", "icc/sgrey-v4", "icc/sgrey-v2-nano", 1, false, 24, 1.0, SampledCurveJustification),
        new("icc/sgrey-v2-nano-to-srgb-v4", "icc/sgrey-v2-nano", "icc/srgb-v4", 1, false, 8, 1.0, SampledCurveJustification),
        new("icc/srgb-v4-to-sgrey-v4", "icc/srgb-v4", "icc/sgrey-v4", 2, false, 2, 0.5, RoundingJustification),
        new("icc/cgats001-to-srgb-v4", "icc/cgats001-compat-v2-micro", "icc/srgb-v4", 1, false, 3, 0.5, GridPointJustification),
        new("icc/cgats001-to-display-p3-v4-perceptual", "icc/cgats001-compat-v2-micro", "icc/display-p3-v4", 0, false, 3, 0.5, GridPointJustification),
        new("icc/cgats001-to-sgrey-v4", "icc/cgats001-compat-v2-micro", "icc/sgrey-v4", 1, false, 3, 0.5, GridPointJustification),
    ];

    private static readonly string[] IntentNames = ["perceptual", "relative-colorimetric", "saturation", "absolute-colorimetric"];

    public static int Run(GeneratorOptions options)
    {
        var transicc = ExecutableFinder.GetFullExecutablePath("transicc") ?? throw new FatalException("Missing required tool: transicc (LittleCMS)");
        var banner = Proc.Exec([transicc]);
        var version = VersionRegex().Match(Encoding.UTF8.GetString(banner.Stdout) + Encoding.UTF8.GetString(banner.Stderr)).Groups[1].Value;
        if (version != PinnedLittleCms && !options.AcceptToolVersions)
            throw new FatalException($"transicc is LittleCMS '{version}' but the corpus is pinned to {PinnedLittleCms}. Review the differences, then rerun with --accept-tool-versions.");

        var profilesDirectory = FullPath.FromPath(options.IccProfiles!);
        return CorpusDriver.Run(options, "mfi-icc-corpus-", output => Generate(output, profilesDirectory, transicc, version),
            (count, files, bytes) => $"{count} color profiles and transforms, {files} files, {bytes} bytes",
            "The committed color management corpus is reproducible.");
    }

    private static int Generate(FullPath outDir, FullPath profilesDirectory, string transicc, string version)
    {
        // Every image fixture is kept as committed: this generator owns the color management sections only
        var manifest = CorpusDriver.LoadCommittedManifest();
        CorpusDriver.CopyCommittedFiles(outDir, CorpusDriver.Fixtures(manifest));

        var profileEntries = new List<object?>();
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var profile in Profiles)
        {
            var data = File.ReadAllBytes(profilesDirectory / profile.FileName);
            var hash = Bytes.Sha256Hex(data);
            if (hash != profile.Sha256)
                throw new FatalException($"{profile.FileName} has sha256 {hash} but {profile.Sha256} is pinned (commit {ProfilesCommit} of {ProfilesRepository}).");

            var path = "icc/profiles/" + profile.FileName;
            paths[profile.Id] = path;
            var target = outDir / path;
            target.CreateParentDirectory();
            File.WriteAllBytes(target, data);
            profileEntries.Add(new Obj
            {
                ["id"] = profile.Id,
                ["file"] = new Obj { ["path"] = path, ["sha256"] = hash, ["role"] = "profile" },
                ["provenance"] = new Obj
                {
                    ["origin"] = "external",
                    ["license"] = "CC0-1.0",
                    ["author"] = "Clinton Ingram",
                    ["source"] = $"{ProfilesRepository}/blob/{ProfilesCommit}/profiles/{profile.FileName}",
                },
                ["features"] = profile.Features.ToList(),
            });
        }

        var transformEntries = new List<object?>();
        foreach (var transform in Transforms)
        {
            var source = Profiles.Single(profile => profile.Id == transform.Source);
            var destination = Profiles.Single(profile => profile.Id == transform.Destination);
            var samples = source.Channels == 4 ? CreateGridPointSamples(File.ReadAllBytes(outDir / paths[source.Id])) : CreateSamples(source.Channels);
            var count = samples.Length / source.Channels;
            List<string> command = [transicc, "-n", "-c0", "-t" + transform.Intent.ToString(CultureInfo.InvariantCulture)];
            if (transform.BlackPointCompensation)
                command.Add("-b");

            string sourcePath = outDir / paths[source.Id];
            string destinationPath = outDir / paths[destination.Id];
            command.AddRange(["-i", sourcePath, "-o", destinationPath]);
            var expected = Convert(command, samples, source.Channels, destination.Channels);

            var vectors = new byte[count * (source.Channels + destination.Channels) * 2];
            var position = 0;
            for (var i = 0; i < count; i++)
            {
                for (var channel = 0; channel < source.Channels; channel++, position += 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(vectors.AsSpan(position), samples[(i * source.Channels) + channel]);
                for (var channel = 0; channel < destination.Channels; channel++, position += 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(vectors.AsSpan(position), expected[(i * destination.Channels) + channel]);
            }

            var vectorsPath = "icc/vectors/" + transform.Id["icc/".Length..] + ".u16le";
            var vectorsTarget = outDir / vectorsPath;
            vectorsTarget.CreateParentDirectory();
            File.WriteAllBytes(vectorsTarget, vectors);
            transformEntries.Add(new Obj
            {
                ["id"] = transform.Id,
                ["source"] = source.Id,
                ["destination"] = destination.Id,
                ["intent"] = IntentNames[transform.Intent],
                ["blackPointCompensation"] = transform.BlackPointCompensation,
                ["sourceChannels"] = source.Channels,
                ["destinationChannels"] = destination.Channels,
                ["sampleCount"] = count,
                ["vectors"] = new Obj { ["path"] = vectorsPath, ["sha256"] = Bytes.Sha256Hex(vectors), ["role"] = "vectors" },
                ["reference"] = new Obj
                {
                    ["tool"] = $"transicc (LittleCMS {version})",
                    ["command"] = string.Join(' ', ["transicc", .. command.Skip(1).Take(command.Count - 5), "-i", paths[source.Id], "-o", paths[destination.Id]]),
                    ["generator"] = $"{ScriptPath} (Generate)",
                },
                ["comparison"] = new Obj
                {
                    ["maxAbsoluteError"] = transform.MaxAbsoluteError,
                    ["maxMeanAbsoluteError"] = transform.MaxMeanAbsoluteError,
                    ["justification"] = transform.Justification,
                },
            });
        }

        manifest["schemaVersion"] = 3;
        manifest["colorProfiles"] = profileEntries;
        manifest["colorTransforms"] = transformEntries;
        CorpusDriver.WriteManifest(outDir, manifest);
        return profileEntries.Count + transformEntries.Count;
    }

    /// <summary>Converts 16-bit samples with transicc: one color per line on its standard input, one per line on its output.</summary>
    private static ushort[] Convert(List<string> command, ushort[] samples, int sourceChannels, int destinationChannels)
    {
        // transicc reads and prints device values on a 0-255 scale, and ink percentages for CMYK
        var sourceScale = sourceChannels == 4 ? 100.0 : 255.0;
        var destinationScale = destinationChannels == 4 ? 100.0 : 255.0;
        var input = new StringBuilder();
        for (var i = 0; i < samples.Length; i++)
        {
            input.Append((samples[i] * sourceScale / 65535).ToString("R", CultureInfo.InvariantCulture));
            input.Append((i + 1) % sourceChannels == 0 ? '\n' : ' ');
        }

        var output = Proc.RunText(command, Encoding.ASCII.GetBytes(input.ToString()));
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("LittleCMS", StringComparison.Ordinal) && !line.StartsWith("Copyright", StringComparison.Ordinal))
            .ToList();
        var count = samples.Length / sourceChannels;
        if (lines.Count != count)
            throw new ToolException($"transicc printed {lines.Count} colors for {count}: {string.Join(' ', command)}");

        var result = new ushort[count * destinationChannels];
        for (var i = 0; i < count; i++)
        {
            var values = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (values.Length != destinationChannels)
                throw new ToolException($"transicc printed '{lines[i]}' instead of {destinationChannels} samples.");

            for (var channel = 0; channel < destinationChannels; channel++)
            {
                var value = double.Parse(values[channel], NumberStyles.Float, CultureInfo.InvariantCulture) / destinationScale;
                result[(i * destinationChannels) + channel] = (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535, MidpointRounding.AwayFromZero);
            }
        }

        return result;
    }

    /// <summary>
    /// The colors of a CMYK conversion: every grid point of the color lookup table of the AToB0 tag (a lut16Type) of the
    /// profile. The device value of a grid point is the one its input table maps to the grid position: the tables are
    /// increasing and interpolated linearly, so each is inverted exactly, then rounded to 16 bits.
    /// </summary>
    private static ushort[] CreateGridPointSamples(byte[] profile)
    {
        var tagCount = Bytes.U32BE(profile, 128);
        byte[]? tag = null;
        for (var i = 0; i < tagCount; i++)
        {
            var entry = 132 + (12 * i);
            if (Bytes.Latin1(profile.AsSpan(entry, 4)) == "A2B0")
                tag = profile.AsSpan((int)Bytes.U32BE(profile, entry + 4), (int)Bytes.U32BE(profile, entry + 8)).ToArray();
        }

        if (tag is null || Bytes.Latin1(tag.AsSpan(0, 4)) != "mft2")
            throw new FatalException("The CMYK profile has no lut16Type AToB0 tag.");

        int channels = tag[8];
        int gridPoints = tag[10];
        int entries = Bytes.U16BE(tag, 48);
        var levels = new ushort[channels][];
        for (var channel = 0; channel < channels; channel++)
        {
            var table = Enumerable.Range(0, entries).Select(i => Bytes.U16BE(tag, 52 + (2 * ((channel * entries) + i))) / 65535.0).ToArray();
            levels[channel] = new ushort[gridPoints];
            for (var point = 0; point < gridPoints; point++)
            {
                var target = (double)point / (gridPoints - 1);
                var index = Array.FindIndex(table, value => value >= target);
                var device = index <= 0 ? 0 : (index - 1 + ((target - table[index - 1]) / (table[index] - table[index - 1]))) / (entries - 1);
                levels[channel][point] = (ushort)Math.Round(device * 65535, MidpointRounding.AwayFromZero);
            }
        }

        var samples = new List<ushort>();
        var indexes = new int[channels];
        while (true)
        {
            for (var channel = 0; channel < channels; channel++)
                samples.Add(levels[channel][indexes[channel]]);

            var last = 0;
            while (last < channels && ++indexes[last] == gridPoints)
                indexes[last++] = 0;
            if (last == channels)
                break;
        }

        return [.. samples];
    }

    /// <summary>
    /// The colors of a grayscale or RGB conversion: a regular grid including both ends of each channel (every 8-bit level
    /// for one channel, 7 levels for three channels), then reproducible pseudo-random colors.
    /// </summary>
    private static ushort[] CreateSamples(int channels)
    {
        var (levels, random) = channels == 1 ? (256, 64) : (7, 157);

        var samples = new List<ushort>();
        var indexes = new int[channels];
        while (true)
        {
            foreach (var index in indexes)
                samples.Add((ushort)Math.Round(index * 65535.0 / (levels - 1), MidpointRounding.AwayFromZero));

            var channel = 0;
            while (channel < channels && ++indexes[channel] == levels)
                indexes[channel++] = 0;
            if (channel == channels)
                break;
        }

        // A linear congruential generator (Numerical Recipes constants): reproducible on every platform
        var state = 0x00C0_FFEEu;
        for (var i = 0; i < random * channels; i++)
        {
            state = (state * 1664525) + 1013904223;
            samples.Add((ushort)(state >> 16));
        }

        return [.. samples];
    }

    [GeneratedRegex(@"\[LittleCMS ([0-9.]+)\]")]
    private static partial Regex VersionRegex();

    private sealed record Profile(string Id, string FileName, string Sha256, int Channels, string[] Features);

    /// <param name="Intent">The ICC rendering intent number, 0 to 3.</param>
    /// <param name="MaxAbsoluteError">The tolerance in 16-bit units.</param>
    private sealed record Transform(string Id, string Source, string Destination, int Intent, bool BlackPointCompensation, int MaxAbsoluteError, double MaxMeanAbsoluteError, string Justification);
}
