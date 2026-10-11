using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the font part of the corpus (the <c>fonts</c> section of the manifest and the fonts/ directory): font files
/// under an allowed license, and the values independent font readers give for them.
/// </summary>
/// <remarks>
/// <para>
/// The synthetic fonts are built by Common/FontBuilder.cs from the descriptions of Common/FontSamples.cs, both shared
/// with the tests and without any dependency on the library. The third-party fonts come from a directory given by
/// --fonts; each file is checked against its pinned SHA-256 before it is used, then subset by <c>hb-subset</c> when its
/// license allows a modified copy (no Reserved Font Name).
/// </para>
/// <para>
/// The reference values (names, metrics, character map, advances, outlines) are read by the HarfBuzz utilities of the
/// pinned meziantou/prebuilt release with two independent implementations: HarfBuzz's own font functions (<c>ot</c>) and
/// FreeType (<c>ft</c>). A value is recorded only when both agree; a disagreement stops the generator. The character
/// map is the join of <c>hb-info --list-unicodes</c> and <c>--list-glyphs</c>, so that it does not go through shaping.
/// </para>
/// <para>
/// The WOFF forms are written by FontBuilder (nothing else in the pinned tools writes WOFF 1.0) and the WOFF2 forms by
/// <c>woff2_compress</c>. Each one is read back through FreeType (<c>--face-loader=ft</c>), the only reader of the pinned
/// tools that opens these containers, and must give the advances and the outlines of the original.
/// </para>
/// </remarks>
internal static partial class FontCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/FontCorpus.cs";
    private const string SamplesPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Common/FontSamples.cs";
    private const string PinnedHarfBuzz = "14.6.0";
    private const string PinnedWoff2 = "1.0.2+fb9c337";

    private static readonly string[] MetricTags = ["hasc", "hdsc", "hlgp", "xhgt", "cpht", "unds", "undo", "strs", "stro"];

    // The tables whose presence is recorded as a feature of the font.
    private static readonly string[] NotableTables =
    [
        "CFF ", "CFF2", "glyf", "kern", "GDEF", "GSUB", "GPOS", "BASE", "fvar", "avar", "gvar", "HVAR", "MVAR", "STAT", "COLR", "CPAL", "sbix", "CBDT", "CBLC", "SVG ",
    ];

    private static readonly SyntheticFont[] SyntheticFonts =
    [
        new("fonts/synthetic-boxes", "fonts/synthetic/boxes.ttf", static () => FontSamples.Boxes().Build(), nameof(FontSamples.Boxes), "ttf", 1),
        new("fonts/synthetic-curves", "fonts/synthetic/curves.ttf", static () => FontSamples.Curves().Build(), nameof(FontSamples.Curves), "ttf", 1),
        new("fonts/synthetic-collection", "fonts/synthetic/collection.ttc", static () => FontBuilder.WriteCollection([FontSamples.Boxes().BuildTables(), FontSamples.Curves().BuildTables()]), nameof(FontSamples.Boxes) + " and " + nameof(FontSamples.Curves), "ttc", 2),
    ];

    private static readonly ExternalFont[] ExternalFonts =
    [
        new(
            "fonts/noto-sans-regular",
            "fonts/noto-sans/NotoSans-Regular.subset.ttf",
            "NotoSans-Regular.ttf",
            "f3961a9cde016d41a4879aecda1474d3a36d6bf54fa0e4643de029cc2248b0e8",
            "https://github.com/notofonts/notofonts.github.io/blob/28b15b4b43b7bed62b5cf6e6b0b5ff5846270535/fonts/NotoSans/unhinted/ttf/NotoSans-Regular.ttf",
            "The Noto Project Authors (https://github.com/notofonts/latin-greek-cyrillic)",
            "OFL-1.1",
            ReservedFontName: null,

            // Basic Latin, Latin-1, a few Latin Extended letters, combining marks, punctuation, the euro sign and the
            // fi and fl ligatures.
            Unicodes: "20-7E,A0-FF,131,152,153,160,161,178,17D,17E,2C6,2DC,300-304,308,30A,327,2010-2014,2018-201E,2020-2022,2026,2030,2039,203A,20AC,2122,FB01,FB02"),
    ];

    // The fonts that are also stored in the other containers.
    private static readonly (string Original, string Flavor)[] Containers =
    [
        ("fonts/synthetic-boxes", "woff"),
        ("fonts/synthetic-boxes", "woff2"),
        ("fonts/synthetic-curves", "woff2"),
        ("fonts/noto-sans-regular", "woff"),
        ("fonts/noto-sans-regular", "woff2"),
    ];

    public static int Run(GeneratorOptions options)
    {
        var tools = new Tools();
        if (tools.HarfBuzzVersion != PinnedHarfBuzz && !options.AcceptToolVersions)
            throw new FatalException($"The HarfBuzz utilities are version '{tools.HarfBuzzVersion}' but the corpus is pinned to {PinnedHarfBuzz}. Review the differences, then rerun with --accept-tool-versions.");

        if (tools.Woff2Version != PinnedWoff2 && !options.AcceptToolVersions)
            throw new FatalException($"The woff2 tools are version '{tools.Woff2Version}' but the corpus is pinned to {PinnedWoff2}. Review the differences, then rerun with --accept-tool-versions.");

        var fontsDirectory = FullPath.FromPath(options.Fonts!);
        return CorpusDriver.Run(options, "mfi-font-corpus-", output => Generate(output, fontsDirectory, tools),
            (count, files, bytes) => $"{count} fonts, {files} files, {bytes} bytes",
            "The committed font corpus is reproducible.");
    }

    private static int Generate(FullPath outDir, FullPath fontsDirectory, Tools tools)
    {
        // Every other part of the corpus is kept as committed: this generator owns the fonts only.
        var manifest = CorpusDriver.LoadCommittedManifest();
        CorpusDriver.CopyCommittedFiles(outDir, CorpusDriver.Fixtures(manifest));
        CorpusDriver.CopyCommittedAv1Vectors(outDir, manifest);
        CorpusDriver.PreserveColorSections(outDir, manifest);

        var work = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-font-work-").FullName);
        try
        {
            var entries = new List<Obj>();
            var files = new Dictionary<string, (string Path, byte[] Data, Obj Provenance, int FaceCount)>(StringComparer.Ordinal);
            foreach (var font in SyntheticFonts)
            {
                var data = font.Build();
                var provenance = new Obj
                {
                    ["origin"] = "generated",
                    ["license"] = "CC0-1.0",
                    ["author"] = "Meziantou.Framework contributors",
                    ["generator"] = $"{SamplesPath} ({font.Function})",
                    ["tools"] = new List<string> { ToolSet.RuntimeLabel },
                };
                files.Add(font.Id, (font.Path, data, provenance, font.FaceCount));
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, font.Flavor, font.FaceCount, "none", reservedFontName: null, original: null, withVectors: true));
            }

            foreach (var font in ExternalFonts)
            {
                var source = File.ReadAllBytes(fontsDirectory / font.FileName);
                var hash = Bytes.Sha256Hex(source);
                if (hash != font.Sha256)
                    throw new FatalException($"{font.FileName} has sha256 {hash} but {font.Sha256} is pinned ({font.Source}).");

                var data = source;
                var modification = "none";
                var provenance = new Obj
                {
                    ["origin"] = "external",
                    ["license"] = font.License,
                    ["author"] = font.Author,
                    ["source"] = font.Source,
                };
                if (font.Unicodes is not null)
                {
                    if (font.ReservedFontName is not null)
                        throw new FatalException($"{font.FileName} has a Reserved Font Name: it cannot be subset.");

                    // Every layout feature is kept, with the glyphs it needs; the hinting instructions are dropped.
                    var input = work / font.FileName;
                    var output = work / ("subset-" + font.FileName);
                    File.WriteAllBytes(input, source);
                    string[] arguments = ["--unicodes=" + font.Unicodes, "--layout-features=*", "--no-hinting", "--notdef-outline"];
                    Proc.Run([tools.HbSubset, .. arguments, "--output-file=" + output, input]);
                    data = File.ReadAllBytes(output);
                    modification = "subset";
                    provenance["tools"] = new List<string> { tools.HarfBuzzLabel("hb-subset") };
                    provenance["commands"] = new List<string> { $"hb-subset {string.Join(' ', arguments)} --output-file={Path.GetFileName(font.Path)} {font.FileName}" };
                }

                files.Add(font.Id, (font.Path, data, provenance, 1));
                entries.Add(CreateEntry(outDir, work, tools, font.Id, font.Path, data, provenance, GetFlavor(data), 1, modification, font.ReservedFontName, original: null, withVectors: true));
            }

            foreach (var (originalId, flavor) in Containers)
            {
                var (originalPath, originalData, originalProvenance, faceCount) = files[originalId];
                var path = Path.ChangeExtension(originalPath, "." + flavor);
                var provenance = new Obj();
                foreach (var (key, value) in originalProvenance)
                {
                    if (key is not ("tools" or "commands" or "generator"))
                    {
                        provenance[key] = value;
                    }
                }

                byte[] data;
                if (flavor == "woff")
                {
                    data = FontBuilder.WriteWoff(originalData);
                    provenance["generator"] = $"{ScriptPath} (FontBuilder.WriteWoff of {originalPath})";
                    provenance["tools"] = new List<string> { ToolSet.RuntimeLabel };
                }
                else
                {
                    var input = work / ("container-" + Path.GetFileName(originalPath));
                    File.WriteAllBytes(input, originalData);
                    Proc.Run([tools.Woff2Compress, input]);
                    data = File.ReadAllBytes(input.ChangeExtension(".woff2"));
                    provenance["generator"] = $"{ScriptPath} (woff2_compress of {originalPath})";
                    provenance["tools"] = new List<string> { tools.Woff2Label };
                    provenance["commands"] = new List<string> { "woff2_compress " + Path.GetFileName(originalPath) };
                }

                if ((string)originalProvenance["origin"]! == "external")
                {
                    // The validator requires a generator for generated files only; the origin stays the one of the font.
                    provenance.Remove("generator");
                }

                CheckContainer(work, tools, originalData, data, flavor, faceCount);
                entries.Add(CreateEntry(outDir, work, tools, originalId + "-" + flavor, path, data, provenance, flavor, faceCount, "container", reservedFontName: null, originalId, withVectors: false));
            }

            manifest["schemaVersion"] = CorpusDriver.SchemaVersion;
            manifest[CorpusDriver.FontsKey] = entries.OrderBy(entry => (string)entry["id"]!, StringComparer.Ordinal).Cast<object?>().ToList();
            CorpusDriver.WriteManifest(outDir, manifest);
            return entries.Count;
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static Obj CreateEntry(FullPath outDir, FullPath work, Tools tools, string id, string path, byte[] data, Obj provenance, string flavor, int faceCount, string modification, string? reservedFontName, string? original, bool withVectors)
    {
        var target = outDir / path;
        target.CreateParentDirectory();
        File.WriteAllBytes(target, data);

        var entry = new Obj
        {
            ["id"] = id,
            ["file"] = new Obj { ["path"] = path, ["sha256"] = Bytes.Sha256Hex(data), ["role"] = "font" },
            ["provenance"] = provenance,
            ["flavor"] = flavor,
            ["faceCount"] = faceCount,
            ["modification"] = modification,
        };
        if (reservedFontName is not null)
        {
            entry["reservedFontName"] = reservedFontName;
        }

        if (original is not null)
        {
            entry["original"] = original;
        }

        if (withVectors)
        {
            entry["features"] = GetFeatures(data);
            var faces = new List<object?>();
            for (var face = 0; face < faceCount; face++)
            {
                faces.Add(RecordFace(work, tools, data, face));
            }

            var vectors = new Obj
            {
                ["tools"] = new List<string> { tools.HarfBuzzLabel("hb-info"), tools.HarfBuzzLabel("hb-shape") + " (--font-funcs=ot and ft)", tools.HarfBuzzLabel("hb-vector") + " (--font-funcs=ot and ft)" },
                ["faces"] = faces,
            };
            var vectorsPath = "fonts/vectors/" + id["fonts/".Length..] + ".json";
            var vectorsData = Encoding.UTF8.GetBytes(PyJson.Dumps(vectors) + "\n");
            var vectorsTarget = outDir / vectorsPath;
            vectorsTarget.CreateParentDirectory();
            File.WriteAllBytes(vectorsTarget, vectorsData);
            entry["vectors"] = new Obj { ["path"] = vectorsPath, ["sha256"] = Bytes.Sha256Hex(vectorsData), ["role"] = "font-vectors" };
        }

        return entry;
    }

    /// <summary>Records what the two font readers say of a face.</summary>
    private static Obj RecordFace(FullPath work, Tools tools, byte[] data, int face)
    {
        var file = work / "record.font";
        File.WriteAllBytes(file, data);
        string[] faceArguments = ["--face-index=" + face.ToString(CultureInfo.InvariantCulture)];

        var info = ParseKeyValues(Proc.RunText([tools.HbInfo, .. faceArguments, file]));
        var glyphCount = int.Parse(info["Glyph count"], CultureInfo.InvariantCulture);

        var metrics = new Obj();
        foreach (var line in Proc.RunText([tools.HbInfo, .. faceArguments, "--list-metrics", file]).Split('\n'))
        {
            // "hasc: horizontal-ascender<tabs>832<tab>": a value marked with * is a fallback the font does not declare.
            var match = MetricRegex().Match(line);
            if (match.Success && MetricTags.Contains(match.Groups["tag"].Value, StringComparer.Ordinal) && !match.Groups["fallback"].Success)
            {
                metrics[match.Groups["tag"].Value] = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
        }

        // The character map without shaping: code point to glyph name, then glyph name to glyph identifier.
        var glyphIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Proc.RunText([tools.HbInfo, .. faceArguments, "--list-glyphs", file]).Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                if (!glyphIds.TryAdd(parts[1], id))
                    throw new FatalException($"The glyph name '{parts[1]}' is used twice: the character map cannot be recorded.");
            }
        }

        if (glyphIds.Count != glyphCount)
            throw new FatalException($"hb-info lists {glyphIds.Count} glyphs but the font has {glyphCount}.");

        var characterMap = new Obj();
        foreach (var line in Proc.RunText([tools.HbInfo, .. faceArguments, "--list-unicodes", file]).Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length == 2 && parts[0].StartsWith("U+", StringComparison.Ordinal))
            {
                characterMap[int.Parse(parts[0].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString("X4", CultureInfo.InvariantCulture)] = glyphIds[parts[1]];
            }
        }

        if (characterMap.Count != int.Parse(info["Unicode count"], CultureInfo.InvariantCulture))
            throw new FatalException("hb-info does not list as many characters as it counts.");

        // The advances of every glyph, without shaping, from the two font readers.
        var advances = new List<int>();
        var outlines = new List<string>();
        for (var start = 0; start < glyphCount; start += 256)
        {
            var glyphs = "[" + string.Join('|', Enumerable.Range(start, Math.Min(256, glyphCount - start)).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
            var withOt = Proc.RunText([tools.HbShape, .. faceArguments, "--font-funcs=ot", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
            var withFt = Proc.RunText([tools.HbShape, .. faceArguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", file, glyphs]);
            if (withOt != withFt)
                throw new FatalException("The two font readers disagree on the advances of the glyphs.");

            foreach (Match match in AdvanceRegex().Matches(withOt))
            {
                advances.Add(int.Parse(match.Groups["advance"].Value, CultureInfo.InvariantCulture));
            }
        }

        if (advances.Count != glyphCount)
            throw new FatalException($"hb-shape gives {advances.Count} advances for {glyphCount} glyphs.");

        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            var text = glyph.ToString(CultureInfo.InvariantCulture);
            var withOt = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ot", "--glyphs", file, text]));
            var withFt = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", file, text]));
            if (withOt != withFt)
                throw new FatalException($"The two font readers disagree on the outline of the glyph {glyph}:\n  ot: {withOt}\n  ft: {withFt}");

            outlines.Add(withOt);
        }

        var result = new Obj
        {
            ["family"] = info["Family"],
            ["subfamily"] = info["Subfamily"],
        };
        if (info.TryGetValue("Postscript name", out var postScriptName) && postScriptName.Length > 0)
        {
            result["postScriptName"] = postScriptName;
        }

        result["unitsPerEm"] = int.Parse(info["Units-Per-EM"], CultureInfo.InvariantCulture);
        result["glyphCount"] = glyphCount;
        result["metrics"] = metrics;
        result["characterMap"] = characterMap;
        result["advances"] = advances.Cast<object?>().ToList();
        result["outlines"] = outlines.Cast<object?>().ToList();
        return result;
    }

    /// <summary>Checks that a container gives, through FreeType, the advances and the outlines of the font it wraps.</summary>
    private static void CheckContainer(FullPath work, Tools tools, byte[] original, byte[] container, string flavor, int faceCount)
    {
        var originalFile = work / "check-original.font";
        var containerFile = work / ("check-container." + flavor);
        File.WriteAllBytes(originalFile, original);
        File.WriteAllBytes(containerFile, container);
        if (flavor == "woff2")
        {
            // The reference decoder accepts the file.
            Proc.Run([tools.Woff2Decompress, containerFile]);
        }

        for (var face = 0; face < faceCount; face++)
        {
            string[] faceArguments = ["--face-index=" + face.ToString(CultureInfo.InvariantCulture)];
            var glyphCount = int.Parse(ParseKeyValues(Proc.RunText([tools.HbInfo, .. faceArguments, originalFile]))["Glyph count"], CultureInfo.InvariantCulture);
            var glyphs = "[" + string.Join('|', Enumerable.Range(0, Math.Min(256, glyphCount)).Select(id => id.ToString(CultureInfo.InvariantCulture))) + "]";
            var expectedAdvances = Proc.RunText([tools.HbShape, .. faceArguments, "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", originalFile, glyphs]);
            var actualAdvances = Proc.RunText([tools.HbShape, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", "--output-format=json", "--no-glyph-names", containerFile, glyphs]);
            if (expectedAdvances != actualAdvances)
                throw new FatalException($"The {flavor} form does not give the advances of the original font.");

            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                var text = glyph.ToString(CultureInfo.InvariantCulture);
                var expected = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--font-funcs=ft", "--glyphs", originalFile, text]));
                var actual = GetOutline(Proc.RunText([tools.HbVector, .. faceArguments, "--face-loader=ft", "--font-funcs=ft", "--glyphs", containerFile, text]));
                if (expected != actual)
                    throw new FatalException($"The {flavor} form does not give the outline of the glyph {glyph} of the original font.");
            }
        }
    }

    private static string GetOutline(string svg)
    {
        // One path per glyph that has an outline; the path data is in font units, with the y axis up.
        var matches = PathRegex().Matches(svg);
        return matches.Count switch
        {
            0 => "",
            1 => matches[0].Groups["data"].Value,
            _ => throw new FatalException("hb-vector drew more than one path for one glyph."),
        };
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(": ", StringComparison.Ordinal);
            if (separator > 0)
            {
                result.TryAdd(line[..separator], line[(separator + 2)..].TrimEnd());
            }
            else if (line.Length > 0 && line[^1] == ':')
            {
                result.TryAdd(line[..^1], "");
            }
        }

        return result;
    }

    private static string GetFlavor(ReadOnlySpan<byte> data)
    {
        return BinaryPrimitives.ReadUInt32BigEndian(data) switch
        {
            0x00010000 or 0x74727565 => "ttf",
            0x4F54544F => "otf",
            0x74746366 => "ttc",
            0x774F4646 => "woff",
            0x774F4632 => "woff2",
            var signature => throw new FatalException($"Unknown font signature 0x{signature:X8}."),
        };
    }

    /// <summary>The feature tags of a font, read from its table directory and from the header of its character map.</summary>
    private static List<string> GetFeatures(ReadOnlySpan<byte> data)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal);
        var faceOffsets = new List<int>();
        if (GetFlavor(data) == "ttc")
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
            for (var i = 0; i < count; i++)
            {
                faceOffsets.Add((int)BinaryPrimitives.ReadUInt32BigEndian(data[(12 + (i * 4))..]));
            }

            features.Add("font.container=ttc");
        }
        else
        {
            faceOffsets.Add(0);
        }

        foreach (var faceOffset in faceOffsets)
        {
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data[(faceOffset + 4)..]);
            for (var i = 0; i < tableCount; i++)
            {
                var record = data.Slice(faceOffset + 12 + (i * 16), 16);
                var tag = Encoding.ASCII.GetString(record[..4]);
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                if (NotableTables.Contains(tag, StringComparer.Ordinal))
                {
                    features.Add("font.table=" + tag.TrimEnd());
                }

                if (tag == "cmap")
                {
                    var cmap = data[offset..];
                    var subtableCount = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
                    for (var j = 0; j < subtableCount; j++)
                    {
                        var subtable = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(4 + (j * 8) + 4)..]);
                        features.Add("font.cmap=" + BinaryPrimitives.ReadUInt16BigEndian(cmap[subtable..]).ToString(CultureInfo.InvariantCulture));
                    }
                }
                else if (tag == "head")
                {
                    features.Add("font.loca=" + (BinaryPrimitives.ReadInt16BigEndian(data[(offset + 50)..]) == 0 ? "short" : "long"));
                }
            }
        }

        return [.. features];
    }

    [GeneratedRegex(@"^(?<tag>[A-Za-z0-9]{4}): \S+\s+(?<value>-?[0-9]+)\s*(?<fallback>\*)?\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex MetricRegex();

    [GeneratedRegex("\"ax\":(?<advance>-?[0-9]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex AdvanceRegex();

    [GeneratedRegex("<path d=\"(?<data>[^\"]*)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex PathRegex();

    [GeneratedRegex(@"\(HarfBuzz\) (?<version>\S+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex HarfBuzzVersionRegex();

    private sealed record SyntheticFont(string Id, string Path, Func<byte[]> Build, string Function, string Flavor, int FaceCount);

    private sealed record ExternalFont(string Id, string Path, string FileName, string Sha256, string Source, string Author, string License, string? ReservedFontName, string? Unicodes);

    private sealed class Tools
    {
        public Tools()
        {
            HbShape = Download(PrebuiltTools.HbShape);
            HbInfo = Download(PrebuiltTools.HbInfo);
            HbSubset = Download(PrebuiltTools.HbSubset);
            HbVector = Download(PrebuiltTools.HbVector);
            Woff2Compress = Download(PrebuiltTools.Woff2Compress);
            Woff2Decompress = Download(PrebuiltTools.Woff2Decompress);
            HarfBuzzVersion = HarfBuzzVersionRegex().Match(Proc.RunText([HbShape, "--version"])).Groups["version"].Value;

            // The woff2 tools print no version: the one of the pinned release is the only statement there is.
            Woff2Version = PrebuiltTools.Woff2Compress.Version;
        }

        public string HbShape { get; }

        public string HbInfo { get; }

        public string HbSubset { get; }

        public string HbVector { get; }

        public string Woff2Compress { get; }

        public string Woff2Decompress { get; }

        public string HarfBuzzVersion { get; }

        public string Woff2Version { get; }

        public string Woff2Label => "woff2 " + Woff2Version;

        public string HarfBuzzLabel(string tool) => tool + " " + HarfBuzzVersion;

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
