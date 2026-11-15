using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// The iccMAX part of the color management corpus: version 5 profiles written as XML (Common/IccMaxProfileXml.cs), built
/// by iccFromXml of iccDEV, and colors converted by its iccApplyNamedCmm (the reference implementation of ICC.2:2023,
/// BSD-3-Clause, from the meziantou/prebuilt release of the Meziantou.Prebuilt package).
/// </summary>
/// <remarks>
/// iccApplyNamedCmm reads a JSON configuration: the sequence of profiles with their rendering intent and the
/// interpolation of their lookup tables (always linear: multilinear, as the library interpolates the color lookup table
/// elements without interpolation hint), and the colors, as floating-point device values or as named colors at full tint.
/// It prints each converted color with 9 decimals, stored rounded to 16 bits.
/// </remarks>
internal static partial class IccCorpus
{
    private const string IccMaxScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/IccCorpus.IccMax.cs";
    private const string XmlBuilderPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Common/IccMaxProfileXml.cs";

    private static readonly string[] IccDevIntents = ["perceptual", "relative", "saturation", "absolute"];

    /// <summary>Gets or sets the iccDEV tools, downloaded by <see cref="Run"/>.</summary>
    private static IccDevTools? IccDev { get; set; }

    /// <summary>Builds a profile from its XML with iccFromXml, which must report a valid profile.</summary>
    private static byte[] BuildFromXml(string xml)
    {
        var directory = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-iccmax-").FullName);
        try
        {
            var xmlPath = directory / "profile.xml";
            var iccPath = directory / "profile.icc";
            File.WriteAllText(xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var result = Proc.Exec([IccDev!.FromXml, xmlPath, iccPath]);
            var output = Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr);
            if (!output.Contains("Profile parsed and saved correctly", StringComparison.Ordinal))
                throw new ToolException($"iccFromXml did not build a valid profile: {output}");

            return File.ReadAllBytes(iccPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The full names of the colors of a named color profile of the corpus, in the order of the profile.</summary>
    private static string[] GetColorNames(Profile profile)
    {
        var (prefix, suffix) = profile.Id == "icc/synthetic-named-lab-v2" ? ("PMS ", " C") : ("", "");
        return [.. IccProfileBuilder.NamedColorSamples.Select(color => prefix + color.Name + suffix)];
    }

    /// <summary>Converts 16-bit samples, or named colors, with iccApplyNamedCmm.</summary>
    /// <returns>The converted samples, and the command to record (the configuration without the colors).</returns>
    private static (ushort[] Expected, string Command) ConvertWithIccDev(FullPath outDir, Transform transform, string sourcePath, string destinationPath, ushort[] samples, string[]? names, int sourceChannels, int destinationChannels)
    {
        var intent = IccDevIntents[transform.Intent];
        var count = names?.Length ?? samples.Length / sourceChannels;
        var colors = new List<object>();
        for (var i = 0; i < count; i++)
        {
            colors.Add(names is not null
                ? new Obj { ["n"] = names[i], ["t"] = 1.0 }
                : new Obj { ["v"] = samples.AsSpan(i * sourceChannels, sourceChannels).ToArray().Select(sample => sample / 65535.0).ToList() });
        }

        var sourceSpace = names is not null ? "nmcl" : sourceChannels switch { 1 => "GRAY", 3 => "RGB ", _ => "CMYK" };
        var configuration = new Obj
        {
            ["profileSequence"] = new List<object>
            {
                new Obj { ["iccFile"] = sourcePath, ["intent"] = intent, ["interpolation"] = "linear" },
                new Obj { ["iccFile"] = destinationPath, ["intent"] = intent, ["interpolation"] = "linear" },
            },
            ["dataFiles"] = new Obj { ["srcType"] = "colorData", ["dstType"] = "legacy", ["dstEncoding"] = "float", ["dstPrecision"] = 9, ["dstDigits"] = 15 },
            ["colorData"] = new Obj { ["encoding"] = "float", ["srcSpace"] = sourceSpace, ["srcEncoding"] = "value", ["data"] = colors },
        };

        var configurationPath = outDir / "iccmax-configuration.json";
        File.WriteAllText(configurationPath, JsonSerializer.Serialize(configuration));
        string output;
        try
        {
            output = Proc.RunText([IccDev!.ApplyNamedCmm, "--cfg", configurationPath]);
        }
        finally
        {
            File.Delete(configurationPath);
        }

        // Each converted color is a line of values, then the source color after a semicolon
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(';')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(values => values.Length > 0 && double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .ToList();
        if (lines.Count != count)
            throw new ToolException($"iccApplyNamedCmm printed {lines.Count} colors for {count}: {output}");

        var result = new ushort[count * destinationChannels];
        for (var i = 0; i < count; i++)
        {
            if (lines[i].Length != destinationChannels)
                throw new ToolException($"iccApplyNamedCmm printed '{string.Join(' ', lines[i])}' instead of {destinationChannels} samples.");

            for (var channel = 0; channel < destinationChannels; channel++)
            {
                var value = double.Parse(lines[i][channel], NumberStyles.Float, CultureInfo.InvariantCulture);
                result[(i * destinationChannels) + channel] = (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535, MidpointRounding.AwayFromZero);
            }
        }

        var command = $"iccApplyNamedCmm --cfg configuration.json (profileSequence: {sourcePath} {intent} linear, {destinationPath} {intent} linear; dstEncoding float, dstPrecision 9; colorData srcSpace '{sourceSpace}', {(names is null ? "float device values" : "named colors at tint 1")})";
        return (result, command);
    }

    private sealed record IccDevTools(string FromXml, string ApplyNamedCmm, string Version);
}
