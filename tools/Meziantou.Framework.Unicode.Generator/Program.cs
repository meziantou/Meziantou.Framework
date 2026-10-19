#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Buffers.Binary;
using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework;
using Meziantou.Framework.Versioning;
using static GeneratorFiles;
using static UcdParsers;

var outputUpdated = false;
if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

// The version of the Unicode Character Database is pinned (tools/Meziantou.Framework.Unicode.Generator/unicode-version.txt):
// the algorithms of the library implement the rules of that version. --check-latest moves the pin to the version
// published as the latest one before generating, so that the data, the conformance files and the rules are
// reviewed together.
Ucd.LoadVersion(root);
foreach (var arg in args)
{
    if (arg is not "--check-latest")
    {
        await Console.Error.WriteLineAsync("Unexpected argument: " + arg);
        await Console.Error.WriteLineAsync("Usage: Meziantou.Framework.Unicode.Generator [--check-latest]");
        return 2;
    }

    var latestVersion = await Ucd.GetLatestVersionAsync();
    if (Version.Parse(latestVersion) > Version.Parse(Ucd.Version))
    {
        Console.WriteLine($"Moving from Unicode {Ucd.Version} to {latestVersion}");
        await File.WriteAllTextAsync(Ucd.GetVersionFilePath(root), latestVersion + "\n");
        Ucd.LoadVersion(root);
        outputUpdated = true;
    }
}

Console.WriteLine($"Unicode {Ucd.Version}");

var outputPath = root / "src" / "Meziantou.Framework.Unicode";
var confusableOutputFilePath = outputPath / "Unicode.Confusables.g.cs";
var unicodeCharacterInfosFilePath = outputPath / "UnicodeCharacterInfos.g.cs";
var unicodeBlocksFilePath = outputPath / "UnicodeBlocks.g.cs";
var unicodeScriptsFilePath = outputPath / "UnicodeScripts.g.cs";
var unicodeDataPath = outputPath / "Resources" / "UnicodeData.bin";
var csprojPath = root / "src" / "Meziantou.Framework.Unicode" / "Meziantou.Framework.Unicode.csproj";
var frameworkCsprojPath = root / "src" / "Meziantou.Framework" / "Meziantou.Framework.csproj";

var (confusableEntries, confusablesLastModified) = await LoadConfusablesEntries();
await WriteConfusableCharactersFile(confusableEntries, confusablesLastModified);
var blockRanges = await LoadBlocksRanges();
await WriteUnicodeBlocksFile(blockRanges);
await WriteUnicodeCharacterInfosFile(blockRanges);
await WriteUnicodeScriptsFile();
if (await PropertyTablesWriter.WriteAsync(outputPath))
    outputUpdated = true;

if (await NormalizationWriter.WriteAsync(outputPath))
    outputUpdated = true;

if (await IdnaWriter.WriteAsync(outputPath))
    outputUpdated = true;

// RemoveDiacritics belongs to Meziantou.Framework: the table changes the version of that package only.
var frameworkUpdated = await DiacriticsWriter.WriteAsync(root / "src" / "Meziantou.Framework" / "StringExtensions.g.cs");
if (frameworkUpdated)
{
    await IncrementPatchVersion(frameworkCsprojPath);
}

// The conformance files only change the tests: they are reported as an update, but they do not change the version
// of the package.
var testFilesUpdated = await ConformanceFilesWriter.WriteAsync(root);

// Update project version
if (outputUpdated)
{
    await IncrementPatchVersion(csprojPath);
}

if (outputUpdated || frameworkUpdated)
{
    Console.WriteLine("The file has been updated");

    // Print git diff to show what changed
    try
    {
        using var gitProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = root,
            },
        };

        gitProcess.Start();
        var outputTask = gitProcess.StandardOutput.ReadToEndAsync();
        var errorTask = gitProcess.StandardError.ReadToEndAsync();
        await gitProcess.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (gitProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            await Console.Out.WriteLineAsync();
            await Console.Out.WriteLineAsync("Git diff:");
            await Console.Out.WriteLineAsync(output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            await Console.Error.WriteLineAsync(error);
        }
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync($"Failed to run git diff: {ex.Message}");
    }

    return 1;
}

if (testFilesUpdated)
{
    Console.WriteLine("The conformance files have been updated");
    return 1;
}

return 0;

static async Task IncrementPatchVersion(FullPath csprojPath)
{
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
    var versionNode = doc.Descendants().First(e => e.Name.LocalName == "Version");
    var version = SemanticVersion.Parse(versionNode.Value);
    versionNode.Value = version.NextPatchVersion().ToString();

    var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, Encoding = encoding, Async = true, };
    // Disposed before returning so the file is complete when the git diff reads it back
    await using var writer = XmlWriter.Create(csprojPath, xws);
    await doc.SaveAsync(writer, CancellationToken.None);
}

static async Task<(List<Entry> entries, string lastModified)> LoadConfusablesEntries()
{
    Console.WriteLine($"  Downloading from: {UcdUrls.Confusables}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.Confusables);
    response.EnsureSuccessStatusCode();

    var lastModified = response.Content.Headers.LastModified?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown";
    Console.WriteLine($"  Last modified: {lastModified}");
    var content = await response.Content.ReadAsStringAsync();

    var entries = new Dictionary<Rune, Entry>();
    var lineCount = 0;
    var processedLines = 0;
    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
            continue;

        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex].Trim();
        }

        if (line.Length == 0)
            continue;

        var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            continue;

        var source = ParseSingleRune(parts[0]);
        var target = ParseCodePointSequence(parts[1]);

        if (!entries.TryAdd(source, new Entry(source, target)))
            throw new InvalidOperationException("Duplicated source mapping: U+" + source.Value.ToString("X", CultureInfo.InvariantCulture));

        processedLines++;
    }

    Console.WriteLine($"  Loaded {entries.Count} confusable mappings from {lineCount} lines ({processedLines} data lines)");
    return (entries.Values.ToList(), lastModified);
}

static async Task<List<UnicodeDataEntry>> LoadUnicodeDataEntries(Dictionary<int, EmojiProperties> emojiPropertiesMap)
{
    Console.WriteLine($"Loading Unicode character data...");
    Console.WriteLine($"  Downloading from: {UcdUrls.UnicodeData}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.UnicodeData);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var entries = new List<UnicodeDataEntry>();
    PendingRange? pendingRange = null;
    var lineCount = 0;

    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0)
            continue;

        var fields = line.Split(';');
        if (fields.Length < 15)
            continue;

        var codePoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var name = fields[1];
        var category = ParseUnicodeCategory(fields[2]);
        var canonicalCombiningClass = byte.Parse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var bidiCategory = ParseBidirectionalCategory(fields[4]);
        var decompositionMapping = NormalizeOptional(fields[5]);
        var decimalDigitValue = ParseSByteOrDefault(fields[6]);
        var digitValue = ParseSByteOrDefault(fields[7]);
        var numericValue = NormalizeOptional(fields[8]);
        var mirrored = fields[9] == "Y";
        var unicode1Name = NormalizeOptional(fields[10]);
        var isoComment = NormalizeOptional(fields[11]);
        var simpleUppercaseMapping = ParseHexOrDefault(fields[12]);
        var simpleLowercaseMapping = ParseHexOrDefault(fields[13]);
        var simpleTitlecaseMapping = ParseHexOrDefault(fields[14]);
        emojiPropertiesMap.TryGetValue(codePoint, out var emojiProps);

        if (!Rune.TryCreate(codePoint, out var rune))
            continue;

        if (name.EndsWith(", First>", StringComparison.Ordinal))
        {
            var rangeName = NormalizeRangeName(name);
            pendingRange = new PendingRange(
                codePoint,
                new UnicodeDataEntry(
                    rune,
                    rangeName,
                    category,
                    bidiCategory,
                    canonicalCombiningClass,
                    decompositionMapping,
                    decimalDigitValue,
                    digitValue,
                    numericValue,
                    mirrored,
                    unicode1Name,
                    isoComment,
                    simpleUppercaseMapping,
                    simpleLowercaseMapping,
                    simpleTitlecaseMapping,
                    emojiProps));
            continue;
        }

        if (name.EndsWith(", Last>", StringComparison.Ordinal))
        {
            if (pendingRange is null)
                throw new InvalidOperationException("UnicodeData range end without start: " + name);

            for (var value = pendingRange.Start; value <= codePoint; value++)
            {
                if (!Rune.TryCreate(value, out var rangeRune))
                    continue;

                emojiPropertiesMap.TryGetValue(value, out var rangeEmojiProps);
                entries.Add(pendingRange.Entry with { Rune = rangeRune, EmojiProperties = rangeEmojiProps });
            }

            pendingRange = null;
            continue;
        }

        entries.Add(new UnicodeDataEntry(
            rune,
            name,
            category,
            bidiCategory,
            canonicalCombiningClass,
            decompositionMapping,
            decimalDigitValue,
            digitValue,
            numericValue,
            mirrored,
            unicode1Name,
            isoComment,
            simpleUppercaseMapping,
            simpleLowercaseMapping,
            simpleTitlecaseMapping,
            emojiProps));
    }

    if (pendingRange is not null)
        throw new InvalidOperationException("UnicodeData range start without end: " + pendingRange.Entry.Name);

    Console.WriteLine($"  Loaded {entries.Count} character entries from {lineCount} lines");
    return entries;
}

static async Task<List<(int Start, int End, string Name)>> LoadBlocksRanges()
{
    Console.WriteLine($"Loading Unicode blocks...");
    Console.WriteLine($"  Downloading from: {UcdUrls.Blocks}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.Blocks);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var blockRanges = new List<(int Start, int End, string Name)>();
    var lineCount = 0;

    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
            continue;

        var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            continue;

        var rangeParts = parts[0].Split("..", StringSplitOptions.TrimEntries);
        if (rangeParts.Length != 2)
        {
            Console.WriteLine($"Warning: Skipping malformed block range: {line}");
            continue;
        }

        if (!int.TryParse(rangeParts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var start) ||
            !int.TryParse(rangeParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var end))
        {
            Console.WriteLine($"Warning: Skipping block range with invalid hex values: {line}");
            continue;
        }

        var blockName = parts[1];
        blockRanges.Add((start, end, blockName));
    }

    Console.WriteLine($"  Loaded {blockRanges.Count} Unicode blocks from {lineCount} lines");
    return blockRanges;
}

static async Task<Dictionary<int, EmojiProperties>> LoadEmojiProperties()
{
    Console.WriteLine($"Loading emoji properties...");
    Console.WriteLine($"  Downloading from: {UcdUrls.EmojiData}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.EmojiData);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var emojiData = new Dictionary<int, EmojiProperties>();
    var lineCount = 0;
    var processedLines = 0;

    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
            continue;

        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex].Trim();
        }

        if (line.Length == 0)
            continue;

        var parts = line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            continue;

        var propertyName = parts[1];
        var property = propertyName switch
        {
            "Emoji" => EmojiProperties.Emoji,
            "Emoji_Presentation" => EmojiProperties.EmojiPresentation,
            "Emoji_Modifier" => EmojiProperties.EmojiModifier,
            "Emoji_Modifier_Base" => EmojiProperties.EmojiModifierBase,
            "Emoji_Component" => EmojiProperties.EmojiComponent,
            "Extended_Pictographic" => EmojiProperties.ExtendedPictographic,
            _ => EmojiProperties.None,
        };

        if (property == EmojiProperties.None)
            continue;

        processedLines++;
        var codePointRange = parts[0];
        if (codePointRange.Contains("..", StringComparison.Ordinal))
        {
            var rangeParts = codePointRange.Split("..", StringSplitOptions.TrimEntries);
            if (rangeParts.Length == 2 &&
                int.TryParse(rangeParts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var start) &&
                int.TryParse(rangeParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var end))
            {
                for (var codePoint = start; codePoint <= end; codePoint++)
                {
                    if (emojiData.TryGetValue(codePoint, out var existing))
                    {
                        emojiData[codePoint] = existing | property;
                    }
                    else
                    {
                        emojiData[codePoint] = property;
                    }
                }
            }
        }
        else
        {
            if (int.TryParse(codePointRange, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
            {
                if (emojiData.TryGetValue(codePoint, out var existing))
                {
                    emojiData[codePoint] = existing | property;
                }
                else
                {
                    emojiData[codePoint] = property;
                }
            }
        }
    }

    Console.WriteLine($"  Loaded emoji properties for {emojiData.Count} code points from {lineCount} lines ({processedLines} data lines)");
    return emojiData;
}

async Task WriteUnicodeCharacterInfosFile(List<(int Start, int End, string Name)> blockRanges)
{
    var emojiProperties = await LoadEmojiProperties();
    var unicodeDataEntries = await LoadUnicodeDataEntries(emojiProperties);
    var unicodeDataBytes = BuildUnicodeDataBinary(unicodeDataEntries);
    if (WriteUnicodeDataBinaryIfChanged(unicodeDataPath, unicodeDataBytes, unicodeDataEntries))
        outputUpdated = true;

    // Names and the sparse string columns are decoded through a single stack buffer, and the
    // front-coding length prefixes are UTF-8 byte counts. Bound both accordingly. Decomposition
    // mappings are rebuilt from code points with a StringBuilder and do not use the buffer.
    var maxStringByteLength = unicodeDataEntries.Max(entry => Math.Max(
        Encoding.UTF8.GetByteCount(entry.Name),
        Math.Max(
            Math.Max(GetByteCount(entry.NumericValue), GetByteCount(entry.Unicode1Name)),
            GetByteCount(entry.IsoComment))));
    var maxStringByteLengthPowerOfTwo = NextPowerOfTwo(maxStringByteLength);

    if (maxStringByteLengthPowerOfTwo > 256)
        throw new InvalidOperationException("Max serialized string length exceeds 256: " + maxStringByteLengthPowerOfTwo);

    static int GetByteCount(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    var emojiCount = unicodeDataEntries.Count(e => e.EmojiProperties != EmojiProperties.None);
    var bytesPerCharacter = unicodeDataEntries.Count > 0 ? (double)unicodeDataBytes.Length / unicodeDataEntries.Count : 0;

    var content = string.Create(CultureInfo.InvariantCulture, $$"""
        // <auto-generated />
        // Generated from Unicode Character Database
        // Data source: {{UcdUrls.UnicodeData}}
        // Binary resource: UnicodeData.bin (uncompressed, see UnicodeDataFormat for the layout)
        // Statistics:
        //   - Total characters: {{unicodeDataEntries.Count}}
        //   - Emoji characters: {{emojiCount}}
        //   - Resource size: {{unicodeDataBytes.Length:N0}} bytes ({{bytesPerCharacter:F2}} bytes per character)
        //   - Max serialized string length: {{maxStringByteLength}} bytes
        //
        // Specification: https://www.unicode.org/reports/tr44/
        // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

        #nullable enable

        namespace Meziantou.Framework;
        internal static partial class UnicodeCharacterInfos
        {
            private const int MaxSerializedStringLength = {{maxStringByteLengthPowerOfTwo}};

            /// <summary>Number of characters written to the binary resource, as counted by the generator.</summary>
            internal const int GeneratedCharacterCount = {{unicodeDataEntries.Count}};
        }
        """);

    if (await WriteTextIfChanged(unicodeCharacterInfosFilePath, content))
        outputUpdated = true;
}

static int NextPowerOfTwo(int value)
{
    if (value <= 1)
        return 1;

    value--;
    value |= value >> 1;
    value |= value >> 2;
    value |= value >> 4;
    value |= value >> 8;
    value |= value >> 16;
    value++;
    return value;
}

static string ParseCodePointSequence(string input)
{
    var sb = new StringBuilder();
    foreach (var token in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        var value = int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        sb.Append(char.ConvertFromUtf32(value));
    }

    return sb.ToString();
}

async Task WriteUnicodeScriptsFile()
{
    Console.WriteLine("Loading Unicode scripts...");
    var longNames = await LoadScriptLongNames();
    var ranges = await LoadScriptRanges(longNames);

    var scriptNames = ranges.Select(r => r.Script).Concat(longNames.Values)
        .Where(name => !string.Equals(name, "Unknown", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal).ToList();
    scriptNames.Sort(StringComparer.Ordinal);

    // Unknown is first so that default(UnicodeScript) is the "no script" value rather than
    // whichever real script happens to sort first.
    scriptNames.Insert(0, "Unknown");

    // The numeric values are compiled into the assemblies that use the enumeration, so a script keeps the value it
    // has in the committed file and a new script takes the next free one, wherever it sorts.
    var identifierIds = StableEnum.Resolve(unicodeScriptsFilePath, "UnicodeScript", scriptNames.Select(ToScriptIdentifier));
    if (identifierIds.Count != scriptNames.Count)
        throw new InvalidOperationException("The committed UnicodeScript enumeration has members that are no longer scripts: decide what to do with them");

    if (identifierIds["Unknown"] != 0)
        throw new InvalidOperationException("UnicodeScript.Unknown must be 0");

    var scriptIds = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var name in scriptNames)
    {
        scriptIds[name] = identifierIds[ToScriptIdentifier(name)];
    }

    var scriptCount = scriptIds.Values.Max() + 1;
    if (scriptCount > byte.MaxValue + 1)
        throw new InvalidOperationException("Too many scripts for a one-byte table: " + scriptCount);

    ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
    for (var i = 1; i < ranges.Count; i++)
    {
        if (ranges[i].Start <= ranges[i - 1].End)
            throw new InvalidOperationException($"Overlapping script ranges at U+{ranges[i].Start:X4}");
    }

    var scriptValues = new byte[Trie.CodePointCount];
    foreach (var range in ranges)
    {
        scriptValues.AsSpan(range.Start, range.End - range.Start + 1).Fill((byte)scriptIds[range.Script]);
    }

    var enumMembers = new StringBuilder();
    foreach (var name in scriptNames.OrderBy(name => scriptIds[name]))
    {
        enumMembers.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>The {EscapeXmlText(name.Replace('_', ' '))} script.</summary>");
        enumMembers.AppendLine(CultureInfo.InvariantCulture, $"    {ToScriptIdentifier(name)} = {scriptIds[name]},");
        enumMembers.AppendLine();
    }

    // ISO 15924 codes: the short names of the values of the Script property.
    var codes = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (alias, longName) in longNames)
    {
        if (alias.Length != 4)
            throw new InvalidOperationException("The short name of a script is not an ISO 15924 code: " + alias);

        codes.TryAdd(longName, alias);
    }

    var codeArms = new StringBuilder();
    foreach (var name in scriptNames.OrderBy(name => scriptIds[name]))
    {
        if (string.Equals(name, "Unknown", StringComparison.Ordinal))
            continue;

        if (!codes.TryGetValue(name, out var code))
            throw new InvalidOperationException("The script has no ISO 15924 code: " + name);

        codeArms.Append(CultureInfo.InvariantCulture, $"            UnicodeScript.{ToScriptIdentifier(name)} => \"{EscapeStringLiteral(code)}\",\n");
    }

    var extensionRanges = await LoadScriptExtensionRanges(longNames, scriptIds);
    var extensionSets = new List<List<int>>();
    var extensionSetIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    var extensionValues = new byte[Trie.CodePointCount];
    foreach (var range in extensionRanges.OrderBy(r => r.Start))
    {
        var key = string.Join(',', range.Scripts);
        if (!extensionSetIndex.TryGetValue(key, out var setIndex))
        {
            setIndex = extensionSets.Count;
            extensionSetIndex[key] = setIndex;
            extensionSets.Add(range.Scripts);
        }

        // 0 means that the code point has no explicit Script_Extensions value.
        if (setIndex + 1 > byte.MaxValue)
            throw new InvalidOperationException("Too many script extension sets for a one-byte table");

        extensionValues.AsSpan(range.Start, range.End - range.Start + 1).Fill((byte)(setIndex + 1));
    }

    // Sets are stored as: count (1 byte) then that many script ids (2 bytes each). The table
    // holds an index into a side table of offsets.
    var setOffsets = new List<int>();
    var setBytes = new List<byte>();
    var maxSetCount = 1;
    foreach (var set in extensionSets)
    {
        setOffsets.Add(setBytes.Count);
        if (set.Count > byte.MaxValue)
            throw new InvalidOperationException("Script extension set is too large: " + set.Count);

        maxSetCount = Math.Max(maxSetCount, set.Count);
        setBytes.Add((byte)set.Count);
        foreach (var id in set)
        {
            WriteUInt16(setBytes, id);
        }
    }

    var setOffsetBytes = new List<byte>();
    foreach (var offset in setOffsets)
    {
        WriteUInt24(setOffsetBytes, offset);
    }

    Console.WriteLine($"  Loaded {extensionRanges.Count} script extension ranges ({extensionSets.Count} distinct sets)");

    var extensionOffsetBlob = FormatBlob(setOffsetBytes);
    var extensionSetBlob = FormatBlob(setBytes);

    var sizes = new List<(string Name, int Bytes)>();
    var tries = new StringBuilder();
    PropertyTablesWriter.AppendTrie(tries, sizes, "ScriptValue", scriptValues, "Script: the numeric value of a UnicodeScript.");
    PropertyTablesWriter.AppendTrie(tries, sizes, "ScriptExtensionSet", extensionValues, "Script_Extensions: 0 when the code point has no explicit value, else 1 + the index of its set in ExtensionSetOffsets.");

    var content = string.Create(CultureInfo.InvariantCulture, $$"""
        // <auto-generated />
        // Generated from Unicode script data
        // Data source: {{UcdUrls.Scripts}}
        // Statistics:
        //   - Scripts: {{scriptNames.Count}}
        //   - Ranges: {{ranges.Count}}
        //   - Encoded size: {{sizes.Sum(size => size.Bytes) + setOffsetBytes.Count + setBytes.Count:N0}} bytes
        //   - Script extension ranges: {{extensionRanges.Count}} ({{extensionSets.Count}} distinct sets)
        //
        // Specification: https://www.unicode.org/reports/tr24/
        // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

        #nullable enable

        using System.Runtime.CompilerServices;
        using System.Text;

        namespace Meziantou.Framework;

        /// <summary>Represents a Unicode script, as defined by the Script character property.</summary>
        /// <remarks>The numeric values are stable across the versions of this package.</remarks>
        /// <seealso href="https://www.unicode.org/reports/tr24/" />
        public enum UnicodeScript : ushort
        {
        {{enumMembers.ToString().TrimEnd()}}
        }

        /// <summary>Provides access to the Unicode Script and Script_Extensions character properties.</summary>
        /// <seealso href="https://www.unicode.org/reports/tr24/" />
        public static partial class UnicodeScripts
        {
            /// <summary>One more than the greatest numeric value of <see cref="UnicodeScript"/>.</summary>
            internal const int ScriptCount = {{scriptCount}};

            /// <summary>The number of scripts of the largest Script_Extensions set.</summary>
            internal const int MaxExtensionCount = {{maxSetCount}};

        {{PropertyTablesWriter.ReadUInt16Method}}
        {{tries.ToString().TrimEnd()}}

            // 3-byte offsets into ExtensionSets.
            private static ReadOnlySpan<byte> ExtensionSetOffsets =>
            [{{extensionOffsetBlob.ToString().TrimEnd().TrimEnd(',')}}
            ];

            // count (1 byte) followed by that many 2-byte script ids.
            private static ReadOnlySpan<byte> ExtensionSets =>
            [{{extensionSetBlob.ToString().TrimEnd().TrimEnd(',')}}
            ];

            /// <summary>Gets the explicit Script_Extensions of a code point as 2-byte script ids, or an empty span when it has none.</summary>
            internal static ReadOnlySpan<byte> GetScriptExtensionsRaw(int codePoint)
            {
                var set = GetScriptExtensionSet(codePoint);
                if (set == 0)
                    return default;

                var offsets = ExtensionSetOffsets;
                var offsetIndex = (set - 1) * 3;
                var setOffset = offsets[offsetIndex] | (offsets[offsetIndex + 1] << 8) | (offsets[offsetIndex + 2] << 16);
                var sets = ExtensionSets;
                var count = sets[setOffset];
                return sets.Slice(setOffset + 1, count * 2);
            }

            private static string? GetIso15924CodeCore(UnicodeScript script)
            {
                return script switch
                {
        {{codeArms.ToString().TrimEnd()}}
                    _ => null,
                };
            }
        }
        """);

    if (await WriteTextIfChanged(unicodeScriptsFilePath, content))
        outputUpdated = true;
}

async Task<Dictionary<string, string>> LoadScriptLongNames()
{
    Console.WriteLine($"  Downloading from: {UcdUrls.PropertyValueAliases}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.PropertyValueAliases);
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync();

    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        if (!line.StartsWith("sc ;", StringComparison.Ordinal))
            continue;

        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            continue;

        result[parts[1]] = parts[2];
    }

    Console.WriteLine($"  Loaded {result.Count} script aliases");
    return result;
}

async Task<List<ScriptRange>> LoadScriptRanges(Dictionary<string, string> longNames)
{
    Console.WriteLine($"  Downloading from: {UcdUrls.Scripts}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.Scripts);
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync();

    var ranges = new List<ScriptRange>();
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex].Trim();
        }

        if (line.Length == 0)
            continue;

        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            continue;

        var rangeParts = parts[0].Split("..", StringSplitOptions.TrimEntries);
        var start = int.Parse(rangeParts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var end = rangeParts.Length > 1
            ? int.Parse(rangeParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : start;

        // Scripts.txt uses long names; normalize anything that appears as an alias.
        var script = longNames.TryGetValue(parts[1], out var longName) ? longName : parts[1];
        ranges.Add(new ScriptRange(start, end, script));
    }

    Console.WriteLine($"  Loaded {ranges.Count} script ranges");
    return ranges;
}

static string ToScriptIdentifier(string scriptName)
{
    var sb = new StringBuilder(scriptName.Length);
    var upperNext = true;
    foreach (var c in scriptName)
    {
        if (c == '_')
        {
            upperNext = true;
            continue;
        }

        sb.Append(upperNext ? char.ToUpperInvariant(c) : c);
        upperNext = false;
    }

    var identifier = sb.ToString();
    if (identifier.Length == 0 || !char.IsLetter(identifier[0]))
        throw new InvalidOperationException($"Script name '{scriptName}' does not yield a valid C# identifier");

    return identifier;
}

static StringBuilder FormatBlob(List<byte> bytes)
{
    var blob = new StringBuilder();
    for (var i = 0; i < bytes.Count; i++)
    {
        if (i % 16 == 0)
        {
            blob.AppendLine();
            blob.Append("        ");
        }
        else
        {
            blob.Append(' ');
        }

        blob.Append(CultureInfo.InvariantCulture, $"0x{bytes[i]:X2},");
    }

    return blob;
}

async Task<List<ScriptExtensionRange>> LoadScriptExtensionRanges(Dictionary<string, string> longNames, Dictionary<string, int> scriptIds)
{
    Console.WriteLine($"  Downloading from: {UcdUrls.ScriptExtensions}");
    using var response = await SharedHttpClient.Instance.GetAsync(UcdUrls.ScriptExtensions);
    response.EnsureSuccessStatusCode();
    var content = await response.Content.ReadAsStringAsync();

    var ranges = new List<ScriptExtensionRange>();
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex].Trim();
        }

        if (line.Length == 0)
            continue;

        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            continue;

        var rangeParts = parts[0].Split("..", StringSplitOptions.TrimEntries);
        var start = int.Parse(rangeParts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var end = rangeParts.Length > 1
            ? int.Parse(rangeParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : start;

        // ScriptExtensions.txt uses the short aliases; map them to the same ids as Scripts.txt.
        var scripts = new List<int>();
        foreach (var code in parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!longNames.TryGetValue(code, out var longName))
                throw new InvalidOperationException("Unknown script alias: " + code);

            if (!scriptIds.TryGetValue(longName, out var id))
                throw new InvalidOperationException("Script has no id: " + longName);

            scripts.Add(id);
        }

        scripts.Sort();
        ranges.Add(new ScriptExtensionRange(start, end, scripts));
    }

    return ranges;
}

static void WriteUInt24(List<byte> destination, int value)
{
    destination.Add((byte)value);
    destination.Add((byte)(value >> 8));
    destination.Add((byte)(value >> 16));
}

static void WriteUInt16(List<byte> destination, int value)
{
    destination.Add((byte)value);
    destination.Add((byte)(value >> 8));
}

static Rune ParseSingleRune(string input)
{
    var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length != 1)
        throw new InvalidOperationException("Source is not a single rune: " + input);

    var value = int.Parse(tokens[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return new Rune(value);
}

// Values below come from files downloaded at build time, so they must never be
// interpolated into generated source without escaping.
static string EscapeStringLiteral(string value)
{
    var sb = new StringBuilder(value.Length);
    foreach (var c in value)
    {
        switch (c)
        {
            case '\\':
                sb.Append("\\\\");
                break;

            case '"':
                sb.Append("\\\"");
                break;

            default:
                if (char.IsControl(c))
                {
                    sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:X4}");
                }
                else
                {
                    sb.Append(c);
                }

                break;
        }
    }

    return sb.ToString();
}

static string EscapeXmlText(string value)
{
    return value.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}

static string EscapeString(string value)
{
    var sb = new StringBuilder();
    foreach (var rune in value.EnumerateRunes())
    {
        var scalar = rune.Value;
        if (scalar <= 0xFFFF)
        {
            sb.Append("\\u");
            sb.Append(scalar.ToString("X4", CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append("\\U");
            sb.Append(scalar.ToString("X8", CultureInfo.InvariantCulture));
        }
    }

    return sb.ToString();
}

static byte[] BuildUnicodeDataBinary(List<UnicodeDataEntry> entries)
{
    var ordered = entries.OrderBy(entry => entry.Rune.Value).ToList();

    var bytes = new List<byte>(capacity: 1 << 20);
    foreach (var b in UnicodeDataFormat.Magic)
    {
        bytes.Add(b);
    }

    bytes.Add(UnicodeDataFormat.LayoutVersion);

    WriteUnicodeProperties(bytes, ordered);
    WriteUnicodeNames(bytes, ordered);
    WriteUnicodeDecompositionMappings(bytes, ordered);
    WriteUnicodeStringColumn(bytes, ordered, nameof(UnicodeDataEntry.NumericValue), entry => entry.NumericValue);
    WriteUnicodeStringColumn(bytes, ordered, nameof(UnicodeDataEntry.Unicode1Name), entry => entry.Unicode1Name);
    WriteUnicodeStringColumn(bytes, ordered, nameof(UnicodeDataEntry.IsoComment), entry => entry.IsoComment);

    Console.WriteLine($"  Serialized {ordered.Count} entries into {bytes.Count} bytes");
    return [.. bytes];
}

// Writes the distinct property tuples and the runs of code points that share them.
static void WriteUnicodeProperties(List<byte> bytes, List<UnicodeDataEntry> entries)
{
    var indexes = new Dictionary<UnicodeCharacterProperties, int>();
    var properties = new List<UnicodeCharacterProperties>();
    var runs = new List<(int Start, int Length, int PropertyIndex)>();

    foreach (var entry in entries)
    {
        var codePoint = entry.Rune.Value;
        var value = CreateUnicodeCharacterProperties(entry);
        if (!indexes.TryGetValue(value, out var index))
        {
            index = properties.Count;
            indexes.Add(value, index);
            properties.Add(value);
        }

        // Extend the previous run when the code points stay contiguous. Unassigned code points
        // and the surrogate range are simply absent, which breaks the run as it should.
        if (runs.Count > 0 && runs[^1].PropertyIndex == index && runs[^1].Start + runs[^1].Length == codePoint)
        {
            runs[^1] = (runs[^1].Start, runs[^1].Length + 1, index);
            continue;
        }

        runs.Add((codePoint, 1, index));
    }

    WriteVarInt(bytes, properties.Count);
    foreach (var value in properties)
    {
        bytes.Add((byte)value.Category);
        bytes.Add((byte)value.BidiCategory);
        bytes.Add(value.CanonicalCombiningClass);
        bytes.Add(unchecked((byte)value.DecimalDigitValue));
        bytes.Add(unchecked((byte)value.DigitValue));
        bytes.Add(value.EmojiProperties);
        bytes.Add(value.Flags);
        WriteZigZagVarInt(bytes, value.SimpleUppercaseDelta);
        WriteZigZagVarInt(bytes, value.SimpleLowercaseDelta);
        WriteZigZagVarInt(bytes, value.SimpleTitlecaseDelta);
    }

    WriteVarInt(bytes, runs.Count);
    var previousEnd = 0;
    foreach (var (start, length, propertyIndex) in runs)
    {
        WriteVarInt(bytes, start - previousEnd);
        WriteVarInt(bytes, length);
        WriteVarInt(bytes, propertyIndex);
        previousEnd = start + length;
    }

    Console.WriteLine($"  {properties.Count} distinct property tuples in {runs.Count} runs");
}

static UnicodeCharacterProperties CreateUnicodeCharacterProperties(UnicodeDataEntry entry)
{
    var codePoint = entry.Rune.Value;
    byte flags = 0;
    if (entry.Mirrored)
    {
        flags |= UnicodeDataFormat.MirroredFlag;
    }

    if (entry.SimpleUppercaseMapping >= 0)
    {
        flags |= UnicodeDataFormat.HasUppercaseFlag;
    }

    if (entry.SimpleLowercaseMapping >= 0)
    {
        flags |= UnicodeDataFormat.HasLowercaseFlag;
    }

    if (entry.SimpleTitlecaseMapping >= 0)
    {
        flags |= UnicodeDataFormat.HasTitlecaseFlag;
    }

    return new UnicodeCharacterProperties(
        entry.Category,
        entry.BidiCategory,
        entry.CanonicalCombiningClass,
        entry.DecimalDigitValue,
        entry.DigitValue,
        (byte)entry.EmojiProperties,
        flags,
        entry.SimpleUppercaseMapping >= 0 ? entry.SimpleUppercaseMapping - codePoint : 0,
        entry.SimpleLowercaseMapping >= 0 ? entry.SimpleLowercaseMapping - codePoint : 0,
        entry.SimpleTitlecaseMapping >= 0 ? entry.SimpleTitlecaseMapping - codePoint : 0);
}

// Writes the names, splitting the ones a whole range shares from the ones that are unique.
static void WriteUnicodeNames(List<byte> bytes, List<UnicodeDataEntry> entries)
{
    var named = entries.Where(entry => !IsSharedUnicodeName(entry.Name)).ToList();

    WriteVarInt(bytes, named.Count);
    var previousCodePoint = 0;
    foreach (var entry in named)
    {
        WriteVarInt(bytes, entry.Rune.Value - previousCodePoint);
        previousCodePoint = entry.Rune.Value;
    }

    // Front-coding restarts at every bucket so a name can be decoded without replaying the
    // whole table; the bucket offsets are what make that random access possible.
    var text = new List<byte>();
    var offsets = new List<int>();
    var previous = Array.Empty<byte>();
    for (var i = 0; i < named.Count; i++)
    {
        if (i % UnicodeDataFormat.NameBucketSize == 0)
        {
            offsets.Add(text.Count);
            previous = [];
        }

        previous = WriteFrontCodedString(text, previous, named[i].Name);
    }

    var previousOffset = 0;
    foreach (var offset in offsets)
    {
        WriteVarInt(bytes, offset - previousOffset);
        previousOffset = offset;
    }

    WriteVarInt(bytes, text.Count);
    bytes.AddRange(text);

    var names = new List<string>();
    var nameIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
    var runs = new List<(int Start, int Length, int NameIndex)>();
    foreach (var entry in entries)
    {
        if (!IsSharedUnicodeName(entry.Name))
            continue;

        var codePoint = entry.Rune.Value;
        if (!nameIndexes.TryGetValue(entry.Name, out var index))
        {
            index = names.Count;
            nameIndexes.Add(entry.Name, index);
            names.Add(entry.Name);
        }

        if (runs.Count > 0 && runs[^1].NameIndex == index && runs[^1].Start + runs[^1].Length == codePoint)
        {
            runs[^1] = (runs[^1].Start, runs[^1].Length + 1, index);
            continue;
        }

        runs.Add((codePoint, 1, index));
    }

    WriteVarInt(bytes, names.Count);
    foreach (var name in names)
    {
        WriteUtf8StringMax255Length(bytes, name);
    }

    WriteVarInt(bytes, runs.Count);
    var previousEnd = 0;
    foreach (var (start, length, nameIndex) in runs)
    {
        WriteVarInt(bytes, start - previousEnd);
        WriteVarInt(bytes, length);
        WriteVarInt(bytes, nameIndex);
        previousEnd = start + length;
    }

    Console.WriteLine($"  {named.Count} unique names in {offsets.Count} buckets ({text.Count} bytes), {names.Count} shared names over {runs.Count} runs");
}

// Whether a name belongs to a range rather than to a single character, such as "<CJK Ideograph>".
static bool IsSharedUnicodeName(string name) => name.Length > 0 && name[0] == '<';

// Writes the decomposition mappings as a tag and code points rather than as hexadecimal text.
static void WriteUnicodeDecompositionMappings(List<byte> bytes, List<UnicodeDataEntry> entries)
{
    var mapped = entries.Where(entry => entry.DecompositionMapping is not null).ToList();
    var parsed = mapped.Select(entry => ParseDecompositionMapping(entry.DecompositionMapping!)).ToList();
    var tags = parsed
        .Select(item => item.Tag)
        .OfType<string>()
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

    var tagIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var tag in tags)
    {
        tagIndexes.Add(tag, tagIndexes.Count + 1);
    }

    WriteVarInt(bytes, tags.Count);
    foreach (var tag in tags)
    {
        WriteUtf8StringMax255Length(bytes, tag);
    }

    WriteVarInt(bytes, mapped.Count);
    var previousCodePoint = 0;
    for (var i = 0; i < mapped.Count; i++)
    {
        var (tag, codePoints) = parsed[i];
        WriteVarInt(bytes, mapped[i].Rune.Value - previousCodePoint);
        previousCodePoint = mapped[i].Rune.Value;

        bytes.Add(tag is null ? (byte)0 : (byte)tagIndexes[tag]);
        if (codePoints.Count > byte.MaxValue)
            throw new InvalidOperationException("Decomposition mapping is too long: " + mapped[i].DecompositionMapping);

        bytes.Add((byte)codePoints.Count);
        foreach (var codePoint in codePoints)
        {
            WriteVarInt(bytes, codePoint);
        }
    }

    Console.WriteLine($"  {mapped.Count} decomposition mappings with {tags.Count} tags");
}

static (string? Tag, List<int> CodePoints) ParseDecompositionMapping(string value)
{
    string? tag = null;
    var rest = value;
    if (value.Length > 0 && value[0] == '<')
    {
        var end = value.IndexOf('>', StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException("Unterminated decomposition tag: " + value);

        tag = value[..(end + 1)];
        rest = value[(end + 1)..];
    }

    var codePoints = new List<int>();
    foreach (var token in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        codePoints.Add(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    return (tag, codePoints);
}

// Writes a property only a few thousand characters have as a sorted side table.
static void WriteUnicodeStringColumn(List<byte> bytes, List<UnicodeDataEntry> entries, string columnName, Func<UnicodeDataEntry, string?> selector)
{
    var present = entries.Where(entry => selector(entry) is not null).ToList();
    var values = present
        .Select(entry => selector(entry)!)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

    var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var value in values)
    {
        indexes.Add(value, indexes.Count);
    }

    WriteVarInt(bytes, values.Count);
    var previous = Array.Empty<byte>();
    foreach (var value in values)
    {
        previous = WriteFrontCodedString(bytes, previous, value);
    }

    WriteVarInt(bytes, present.Count);
    var previousCodePoint = 0;
    foreach (var entry in present)
    {
        WriteVarInt(bytes, entry.Rune.Value - previousCodePoint);
        previousCodePoint = entry.Rune.Value;
        WriteVarInt(bytes, indexes[selector(entry)!]);
    }

    Console.WriteLine($"  {columnName}: {present.Count} entries, {values.Count} distinct values");
}

static bool WriteUnicodeDataBinaryIfChanged(FullPath filePath, byte[] content, IReadOnlyList<UnicodeDataEntry> entries)
{
    if (File.Exists(filePath))
    {
        var existing = File.ReadAllBytes(filePath);
        if (existing.AsSpan().SequenceEqual(content))
            return false;

        var existingEntries = ReadUnicodeDataBinary(existing);
        if (existingEntries is null)
        {
            Console.WriteLine("The existing Unicode data binary uses a different layout. Rewriting it.");
        }
        else
        {
            var diff = DiffUnicodeData(existingEntries, entries);
            if (!diff.HasChanges)
            {
                Console.WriteLine("Unicode data binary differs but no semantic changes were detected. Skipping write.");
                return false;
            }

            WriteUnicodeDataDiff(diff);
        }
    }

    filePath.CreateParentDirectory();
    File.WriteAllBytes(filePath, content);
    return true;
}

// Reads back a resource written by BuildUnicodeDataBinary so changes can be diffed. Returns null
// when the resource is not in the current layout.
static Dictionary<Rune, UnicodeDataEntry>? ReadUnicodeDataBinary(byte[] content)
{
    var span = content.AsSpan();
    if (span.Length < UnicodeDataFormat.HeaderLength
        || !span[..UnicodeDataFormat.Magic.Length].SequenceEqual(UnicodeDataFormat.Magic)
        || span[UnicodeDataFormat.Magic.Length] != UnicodeDataFormat.LayoutVersion)
    {
        return null;
    }

    var position = UnicodeDataFormat.HeaderLength;

    var properties = new UnicodeCharacterProperties[UnicodeDataFormat.ReadVarInt(span, ref position)];
    for (var i = 0; i < properties.Length; i++)
    {
        var fixedPart = span.Slice(position, UnicodeDataFormat.PropertyFixedLength);
        position += UnicodeDataFormat.PropertyFixedLength;
        properties[i] = new UnicodeCharacterProperties(
            (UnicodeCategory)fixedPart[0],
            (UnicodeBidirectionalCategory)fixedPart[1],
            fixedPart[2],
            (sbyte)fixedPart[3],
            (sbyte)fixedPart[4],
            fixedPart[5],
            fixedPart[6],
            UnicodeDataFormat.ReadZigZagVarInt(span, ref position),
            UnicodeDataFormat.ReadZigZagVarInt(span, ref position),
            UnicodeDataFormat.ReadZigZagVarInt(span, ref position));
    }

    var codePoints = new List<int>();
    var codePointProperties = new List<UnicodeCharacterProperties>();
    var runCount = UnicodeDataFormat.ReadVarInt(span, ref position);
    var previousEnd = 0;
    for (var i = 0; i < runCount; i++)
    {
        var start = previousEnd + UnicodeDataFormat.ReadVarInt(span, ref position);
        var length = UnicodeDataFormat.ReadVarInt(span, ref position);
        var value = properties[UnicodeDataFormat.ReadVarInt(span, ref position)];
        for (var codePoint = start; codePoint < start + length; codePoint++)
        {
            codePoints.Add(codePoint);
            codePointProperties.Add(value);
        }

        previousEnd = start + length;
    }

    var names = new Dictionary<int, string>();
    var namedCount = UnicodeDataFormat.ReadVarInt(span, ref position);
    var namedCodePoints = ReadCodePointList(span, ref position, namedCount);
    var bucketCount = (namedCount + UnicodeDataFormat.NameBucketSize - 1) / UnicodeDataFormat.NameBucketSize;
    for (var i = 0; i < bucketCount; i++)
    {
        UnicodeDataFormat.ReadVarInt(span, ref position);
    }

    var nameTextLength = UnicodeDataFormat.ReadVarInt(span, ref position);
    var nameTextEnd = position + nameTextLength;
    var buffer = new byte[byte.MaxValue + 1];
    for (var i = 0; i < namedCount; i++)
    {
        // The writer resets the shared prefix at every bucket, and reading forward replays
        // exactly that, so the bucket offsets are only needed for random access.
        var length = ReadFrontCodedString(span, ref position, buffer);
        names.Add(namedCodePoints[i], Encoding.UTF8.GetString(buffer.AsSpan(0, length)));
    }

    Debug.Assert(position == nameTextEnd, "The name text section was not fully consumed.");
    position = nameTextEnd;

    var sharedNames = ReadStringList(span, ref position);
    var sharedNameRunCount = UnicodeDataFormat.ReadVarInt(span, ref position);
    previousEnd = 0;
    for (var i = 0; i < sharedNameRunCount; i++)
    {
        var start = previousEnd + UnicodeDataFormat.ReadVarInt(span, ref position);
        var length = UnicodeDataFormat.ReadVarInt(span, ref position);
        var name = sharedNames[UnicodeDataFormat.ReadVarInt(span, ref position)];
        for (var codePoint = start; codePoint < start + length; codePoint++)
        {
            names.Add(codePoint, name);
        }

        previousEnd = start + length;
    }

    var tags = ReadStringList(span, ref position);
    var decompositions = new Dictionary<int, string>();
    var decompositionCount = UnicodeDataFormat.ReadVarInt(span, ref position);
    var previousCodePoint = 0;
    for (var i = 0; i < decompositionCount; i++)
    {
        previousCodePoint += UnicodeDataFormat.ReadVarInt(span, ref position);
        var tag = span[position];
        position++;
        var mappingLength = span[position];
        position++;

        var builder = new StringBuilder();
        if (tag != 0)
        {
            builder.Append(tags[tag - 1]);
        }

        for (var j = 0; j < mappingLength; j++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CultureInfo.InvariantCulture, $"{UnicodeDataFormat.ReadVarInt(span, ref position):X4}");
        }

        decompositions.Add(previousCodePoint, builder.ToString());
    }

    var numericValues = ReadUnicodeStringColumn(span, ref position);
    var unicode1Names = ReadUnicodeStringColumn(span, ref position);
    var isoComments = ReadUnicodeStringColumn(span, ref position);

    var entries = new Dictionary<Rune, UnicodeDataEntry>(codePoints.Count);
    for (var i = 0; i < codePoints.Count; i++)
    {
        var codePoint = codePoints[i];
        var value = codePointProperties[i];
        entries.Add(new Rune(codePoint), new UnicodeDataEntry(
            new Rune(codePoint),
            names.GetValueOrDefault(codePoint, ""),
            value.Category,
            value.BidiCategory,
            value.CanonicalCombiningClass,
            decompositions.GetValueOrDefault(codePoint),
            value.DecimalDigitValue,
            value.DigitValue,
            numericValues.GetValueOrDefault(codePoint),
            (value.Flags & UnicodeDataFormat.MirroredFlag) != 0,
            unicode1Names.GetValueOrDefault(codePoint),
            isoComments.GetValueOrDefault(codePoint),
            value.GetMapping(codePoint, UnicodeDataFormat.HasUppercaseFlag),
            value.GetMapping(codePoint, UnicodeDataFormat.HasLowercaseFlag),
            value.GetMapping(codePoint, UnicodeDataFormat.HasTitlecaseFlag),
            (EmojiProperties)value.EmojiProperties));
    }

    return entries;
}

static Dictionary<int, string> ReadUnicodeStringColumn(ReadOnlySpan<byte> data, ref int position)
{
    var buffer = new byte[byte.MaxValue + 1];
    var values = new string[UnicodeDataFormat.ReadVarInt(data, ref position)];
    for (var i = 0; i < values.Length; i++)
    {
        var length = ReadFrontCodedString(data, ref position, buffer);
        values[i] = Encoding.UTF8.GetString(buffer.AsSpan(0, length));
    }

    var result = new Dictionary<int, string>();
    var count = UnicodeDataFormat.ReadVarInt(data, ref position);
    var codePoint = 0;
    for (var i = 0; i < count; i++)
    {
        codePoint += UnicodeDataFormat.ReadVarInt(data, ref position);
        result.Add(codePoint, values[UnicodeDataFormat.ReadVarInt(data, ref position)]);
    }

    return result;
}

static List<int> ReadCodePointList(ReadOnlySpan<byte> data, ref int position, int count)
{
    var values = new List<int>(count);
    var codePoint = 0;
    for (var i = 0; i < count; i++)
    {
        codePoint += UnicodeDataFormat.ReadVarInt(data, ref position);
        values.Add(codePoint);
    }

    return values;
}

static List<string> ReadStringList(ReadOnlySpan<byte> data, ref int position)
{
    var values = new List<string>();
    var count = UnicodeDataFormat.ReadVarInt(data, ref position);
    for (var i = 0; i < count; i++)
    {
        var length = data[position];
        position++;
        values.Add(Encoding.UTF8.GetString(data.Slice(position, length)));
        position += length;
    }

    return values;
}

static int ReadFrontCodedString(ReadOnlySpan<byte> data, ref int position, byte[] buffer)
{
    var sharedPrefixLength = data[position];
    var suffixLength = data[position + 1];
    position += 2;
    data.Slice(position, suffixLength).CopyTo(buffer.AsSpan(sharedPrefixLength));
    position += suffixLength;
    return sharedPrefixLength + suffixLength;
}

static UnicodeDataDiff DiffUnicodeData(Dictionary<Rune, UnicodeDataEntry> existingEntries, IReadOnlyList<UnicodeDataEntry> newEntries)
{
    var newEntriesByRune = new Dictionary<Rune, UnicodeDataEntry>(newEntries.Count);
    foreach (var entry in newEntries)
    {
        newEntriesByRune.TryAdd(entry.Rune, entry);
    }

    var added = new List<UnicodeDataEntry>();
    var removed = new List<UnicodeDataEntry>();
    var changed = new List<UnicodeDataChangedEntry>();

    foreach (var item in newEntriesByRune.OrderBy(entry => entry.Key.Value))
    {
        if (!existingEntries.TryGetValue(item.Key, out var existingEntry))
        {
            added.Add(item.Value);
            continue;
        }

        var differences = GetEntryDifferences(existingEntry, item.Value);
        if (differences.Count > 0)
            changed.Add(new UnicodeDataChangedEntry(existingEntry, item.Value, differences));
    }

    foreach (var item in existingEntries.OrderBy(entry => entry.Key.Value))
    {
        if (!newEntriesByRune.ContainsKey(item.Key))
            removed.Add(item.Value);
    }

    return new UnicodeDataDiff(added, removed, changed);
}

static List<string> GetEntryDifferences(UnicodeDataEntry existingEntry, UnicodeDataEntry newEntry)
{
    var differences = new List<string>();

    AddDifferenceIfChanged("Name", existingEntry.Name, newEntry.Name);
    AddDifferenceIfChanged("Category", existingEntry.Category, newEntry.Category);
    AddDifferenceIfChanged("BidiCategory", existingEntry.BidiCategory, newEntry.BidiCategory);
    AddDifferenceIfChanged("CanonicalCombiningClass", existingEntry.CanonicalCombiningClass, newEntry.CanonicalCombiningClass);
    AddDifferenceIfChanged("DecompositionMapping", existingEntry.DecompositionMapping, newEntry.DecompositionMapping);
    AddDifferenceIfChanged("DecimalDigitValue", existingEntry.DecimalDigitValue, newEntry.DecimalDigitValue);
    AddDifferenceIfChanged("DigitValue", existingEntry.DigitValue, newEntry.DigitValue);
    AddDifferenceIfChanged("NumericValue", existingEntry.NumericValue, newEntry.NumericValue);
    AddDifferenceIfChanged("Mirrored", existingEntry.Mirrored, newEntry.Mirrored);
    AddDifferenceIfChanged("Unicode1Name", existingEntry.Unicode1Name, newEntry.Unicode1Name);
    AddDifferenceIfChanged("IsoComment", existingEntry.IsoComment, newEntry.IsoComment);
    AddDifferenceIfChanged("SimpleUppercaseMapping", existingEntry.SimpleUppercaseMapping, newEntry.SimpleUppercaseMapping);
    AddDifferenceIfChanged("SimpleLowercaseMapping", existingEntry.SimpleLowercaseMapping, newEntry.SimpleLowercaseMapping);
    AddDifferenceIfChanged("SimpleTitlecaseMapping", existingEntry.SimpleTitlecaseMapping, newEntry.SimpleTitlecaseMapping);
    AddDifferenceIfChanged("EmojiProperties", existingEntry.EmojiProperties, newEntry.EmojiProperties);

    return differences;

    void AddDifferenceIfChanged<T>(string name, T existingValue, T newValue)
    {
        if (EqualityComparer<T>.Default.Equals(existingValue, newValue))
            return;

        differences.Add($"{name}: {FormatValue(existingValue)} -> {FormatValue(newValue)}");
    }
}

static void WriteUnicodeDataDiff(UnicodeDataDiff diff)
{
    Console.WriteLine("Unicode data binary diff (semantic):");
    Console.WriteLine($"  Added: {diff.Added.Count.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"  Removed: {diff.Removed.Count.ToString(CultureInfo.InvariantCulture)}");
    Console.WriteLine($"  Changed: {diff.Changed.Count.ToString(CultureInfo.InvariantCulture)}");

    WriteEntrySample("Added", diff.Added, FormatEntry);
    WriteEntrySample("Removed", diff.Removed, FormatEntry);
    WriteChangedSample(diff.Changed);

    void WriteEntrySample(string label, List<UnicodeDataEntry> entries, Func<UnicodeDataEntry, string> formatter)
    {
        const int MaxSamples = 20;
        if (entries.Count == 0)
            return;

        Console.WriteLine($"  {label} (first {Math.Min(MaxSamples, entries.Count).ToString(CultureInfo.InvariantCulture)}):");
        foreach (var entry in entries.Take(MaxSamples))
        {
            Console.WriteLine($"    - {formatter(entry)}");
        }

        if (entries.Count > MaxSamples)
        {
            Console.WriteLine($"    ... {(entries.Count - MaxSamples).ToString(CultureInfo.InvariantCulture)} more");
        }
    }

    void WriteChangedSample(List<UnicodeDataChangedEntry> entries)
    {
        const int MaxSamples = 20;
        if (entries.Count == 0)
            return;

        Console.WriteLine($"  Changed (first {Math.Min(MaxSamples, entries.Count).ToString(CultureInfo.InvariantCulture)}):");
        foreach (var entry in entries.Take(MaxSamples))
        {
            Console.WriteLine($"    - {FormatEntry(entry.Current)}: {string.Join(", ", entry.Differences)}");
        }

        if (entries.Count > MaxSamples)
        {
            Console.WriteLine($"    ... {(entries.Count - MaxSamples).ToString(CultureInfo.InvariantCulture)} more");
        }
    }
}

static string FormatEntry(UnicodeDataEntry entry)
{
    return string.Create(CultureInfo.InvariantCulture, $"U+{entry.Rune.Value:X4} {entry.Name}");
}

static string FormatValue<T>(T value)
{
    if (value is null)
        return "<null>";

    if (value is string text)
        return $"\"{text}\"";

    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
}

static string NormalizeRangeName(string name)
{
    return name.Replace(", First>", ">", StringComparison.Ordinal)
        .Replace(", Last>", ">", StringComparison.Ordinal);
}

static string? NormalizeOptional(string value)
{
    return string.IsNullOrEmpty(value) ? null : value;
}

static int ParseHexOrDefault(string value)
{
    return string.IsNullOrEmpty(value)
        ? -1
        : int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

static sbyte ParseSByteOrDefault(string value)
{
    return string.IsNullOrEmpty(value)
        ? (sbyte)-1
        : sbyte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
}

static void WriteUtf8StringMax255Length(List<byte> destination, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    if (bytes.Length > byte.MaxValue)
        throw new InvalidOperationException("String exceeds the maximum length of 255 bytes: " + value);

    destination.Add((byte)bytes.Length);
    destination.AddRange(bytes);
}

// Writes a string as the number of bytes it shares with the previous one and the rest. Returns the
// UTF-8 bytes of the value, to pass as the next call's previous.
static byte[] WriteFrontCodedString(List<byte> destination, byte[] previous, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    if (bytes.Length > byte.MaxValue)
        throw new InvalidOperationException("String exceeds the maximum length of 255 bytes: " + value);

    // The prefix is measured in bytes, so it can land inside a multi-byte character. That is
    // fine because a reader rebuilds the bytes and decodes the whole string at the end.
    var sharedPrefixLength = 0;
    var max = Math.Min(previous.Length, bytes.Length);
    while (sharedPrefixLength < max && previous[sharedPrefixLength] == bytes[sharedPrefixLength])
    {
        sharedPrefixLength++;
    }

    destination.Add((byte)sharedPrefixLength);
    destination.Add((byte)(bytes.Length - sharedPrefixLength));
    destination.AddRange(bytes.AsSpan(sharedPrefixLength));
    return bytes;
}

static void WriteVarInt(List<byte> destination, int value)
{
    if (value < 0)
        throw new ArgumentOutOfRangeException(nameof(value), value, "The value must not be negative.");

    var remaining = (uint)value;
    while (remaining >= 0x80)
    {
        destination.Add((byte)(remaining | 0x80));
        remaining >>= 7;
    }

    destination.Add((byte)remaining);
}

static void WriteZigZagVarInt(List<byte> destination, int value)
{
    WriteVarInt(destination, (int)(((uint)value << 1) ^ (uint)(value >> 31)));
}

async Task WriteConfusableCharactersFile(List<Entry> confusableEntries, string confusablesLastModified)
{
    var ordered = confusableEntries.OrderBy(entry => entry.Source.Value).ToList();

    // A three-stage table gives, for each code point, 0 when it is not confusable, or the rank of its mapping among
    // the mappings of its page of 128 code points, plus 1. With the index of the first mapping of each page, a lookup
    // is a few array reads and no search. The replacements are stored once in a pool of UTF-16 code units.
    const int PageShift = 7;
    var confusable = new byte[Trie.CodePointCount];
    var pageCount = (ordered[^1].Source.Value >> PageShift) + 1;
    var pageStarts = new ushort[pageCount];
    var nextPage = 0;
    var pool = new StringBuilder();
    var poolOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
    var records = new List<byte>();
    var maxLength = 0;
    for (var index = 0; index < ordered.Count; index++)
    {
        var entry = ordered[index];
        var page = entry.Source.Value >> PageShift;
        while (nextPage <= page)
        {
            pageStarts[nextPage++] = checked((ushort)index);
        }

        confusable[entry.Source.Value] = checked((byte)(index - pageStarts[page] + 1));
        if (!poolOffsets.TryGetValue(entry.Target, out var offset))
        {
            offset = pool.Length;
            poolOffsets.Add(entry.Target, offset);
            pool.Append(entry.Target);
        }

        if (entry.Target.Length > byte.MaxValue)
            throw new InvalidOperationException("The replacement of U+" + entry.Source.Value.ToString("X4", CultureInfo.InvariantCulture) + " is too long");

        if (offset > ushort.MaxValue)
            throw new InvalidOperationException("The pool of the confusable replacements is too large for a two-byte offset");

        maxLength = Math.Max(maxLength, entry.Target.Length);
        records.Add((byte)offset);
        records.Add((byte)(offset >> 8));
        records.Add((byte)entry.Target.Length);
    }

    var sizes = new List<(string Name, int Bytes)>();
    var tables = new StringBuilder();
    PropertyTablesWriter.AppendTrie(tables, sizes, "Confusable", confusable, """
        0 when the code point is not confusable; otherwise 1 plus the rank of its mapping among the mappings of its page
        of 128 code points.
        """);
    var pageStartBytes = new byte[pageStarts.Length * 2];
    for (var i = 0; i < pageStarts.Length; i++)
    {
        pageStartBytes[i * 2] = (byte)pageStarts[i];
        pageStartBytes[(i * 2) + 1] = (byte)(pageStarts[i] >> 8);
    }

    PropertyTablesWriter.AppendBytes(tables, sizes, "ConfusablePageStarts", pageStartBytes,
        "For each page of 128 code points up to the last confusable one, the index of its first mapping in ConfusableMappings, 2 bytes little-endian.");
    PropertyTablesWriter.AppendBytes(tables, sizes, "ConfusableMappings", [.. records],
        "3-byte entries sorted by code point: the offset (2 bytes) and the length (1 byte) of the replacement in Pool, in UTF-16 code units, little-endian.");

    // The pool is a string constant rather than bytes: a replacement is a slice of it, appended without decoding.
    var poolText = pool.ToString();
    var poolLines = new StringBuilder();
    var poolRunes = poolText.EnumerateRunes().ToArray();
    for (var i = 0; i < poolRunes.Length; i += 16)
    {
        poolLines.Append(poolLines.Length == 0 ? "        \"" : "        + \"");
        poolLines.Append(EscapeString(string.Concat(poolRunes.Skip(i).Take(16).Select(rune => rune.ToString()))));
        poolLines.Append("\"\n");
    }

    sizes.Add(("Pool", poolText.Length * 2));

    var multiCharMappings = ordered.Count(e => e.Target.EnumerateRunes().Count() > 1);
    var singleCharMappings = ordered.Count - multiCharMappings;
    var sizeLines = string.Join('\n', sizes.Select(size => string.Create(CultureInfo.InvariantCulture, $"//   - {size.Name}: {size.Bytes:N0} bytes")));

    var result = $$"""
    // <auto-generated />
    // Generated from Unicode Security Confusables data
    // Data source: {{UcdUrls.Confusables}}
    // Last modified: {{confusablesLastModified}}
    // Statistics:
    //   - Total confusable mappings: {{ordered.Count.ToString(CultureInfo.InvariantCulture)}}
    //   - Single-character replacements: {{singleCharMappings.ToString(CultureInfo.InvariantCulture)}}
    //   - Multi-character replacements: {{multiCharMappings.ToString(CultureInfo.InvariantCulture)}}
    // Sizes ({{sizes.Sum(size => size.Bytes).ToString("N0", CultureInfo.InvariantCulture)}} bytes):
    {{sizeLines}}
    //
    // Specification: https://www.unicode.org/reports/tr39/
    // Purpose: Helps detect visually confusable characters that may be used in security attacks
    // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

    #nullable enable

    using System.Runtime.CompilerServices;

    namespace Meziantou.Framework;

    /// <summary>The mappings of the confusable characters to their prototypes.</summary>
    internal static class UnicodeConfusablesData
    {
        /// <summary>The number of confusable code points.</summary>
        internal const int Count = {{ordered.Count.ToString(CultureInfo.InvariantCulture)}};

        /// <summary>The greatest length, in UTF-16 code units, of the replacement of a code point.</summary>
        internal const int MaxReplacementLength = {{maxLength.ToString(CultureInfo.InvariantCulture)}};

    {{PropertyTablesWriter.ReadUInt16Method}}

        /// <summary>Determines whether a code point has a replacement.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsConfusable(int codePoint) => GetConfusable(codePoint) != 0;

        /// <summary>Finds the mapping of a code point.</summary>
        /// <returns>The index of the mapping, between 0 and <see cref="Count"/> excluded, or -1 when the code point is not confusable.</returns>
        public static int FindMapping(int codePoint)
        {
            var rank = GetConfusable(codePoint);
            if (rank == 0)
                return -1;

            return ReadUInt16(ConfusablePageStarts, (codePoint >> {{PageShift}}) * 2) + rank - 1;
        }

        /// <summary>Gets the replacement of a mapping found by <see cref="FindMapping(int)"/>.</summary>
        public static ReadOnlySpan<char> GetReplacement(int mapping)
        {
            var offset = mapping * 3;
            var records = ConfusableMappings;
            var start = records[offset] | (records[offset + 1] << 8);
            return Pool.AsSpan(start, records[offset + 2]);
        }

        /// <summary>Tries to get the replacement of a code point.</summary>
        public static bool TryGetReplacement(int codePoint, out ReadOnlySpan<char> replacement)
        {
            var mapping = FindMapping(codePoint);
            if (mapping < 0)
            {
                replacement = default;
                return false;
            }

            replacement = GetReplacement(mapping);
            return true;
        }
    {{tables.ToString().TrimEnd()}}

        // The replacements, in UTF-16 code units.
        private const string Pool =
    {{poolLines.ToString().TrimEnd('\n')}};
    }

    """.ReplaceLineEndings("\n");

    if (await WriteTextIfChanged(confusableOutputFilePath, result))
        outputUpdated = true;
}

async Task WriteUnicodeBlocksFile(List<(int Start, int End, string Name)> blockRanges)
{
    var sb = new StringBuilder();

    // Generate property for each block
    foreach (var (start, end, name) in blockRanges.OrderBy(b => b.Start))
    {
        var propertyName = ToPropertyName(name);
        var codePointCount = end - start + 1;
        sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>{EscapeXmlText(name)} (U+{start:X4}..U+{end:X4}, {codePointCount:N0} code points).</summary>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    public static UnicodeBlock {propertyName} {{ get; }} = UnicodeBlock.CreateInternal(\"{EscapeStringLiteral(name)}\", new UnicodeRange(0x{start:X}, 0x{end:X}));");
        sb.AppendLine();
    }

    var totalCodePoints = blockRanges.Sum(b => b.End - b.Start + 1);

    // Generate GetBlock method with binary search
    sb.AppendLine("    /// <summary>Gets the Unicode block for a code point.</summary>");
    sb.AppendLine("    /// <param name=\"codePoint\">The code point.</param>");
    sb.AppendLine("    /// <returns>The Unicode block, or <see cref=\"Unknown\"/> if not found.</returns>");
    sb.AppendLine("    public static UnicodeBlock GetBlock(int codePoint)");
    sb.AppendLine("    {");

    // Generate switch expression with ranges for better performance
    sb.AppendLine("        return codePoint switch");
    sb.AppendLine("        {");

    foreach (var (start, end, name) in blockRanges.OrderBy(b => b.Start))
    {
        var propertyName = ToPropertyName(name);
        sb.AppendLine($"            >= 0x{start:X} and <= 0x{end:X} => {propertyName},");
    }

    sb.AppendLine("            _ => Unknown,");
    sb.AppendLine("        };");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    /// <summary>Gets the Unicode block for a rune.</summary>");
    sb.AppendLine("    /// <param name=\"rune\">The rune.</param>");
    sb.AppendLine("    /// <returns>The Unicode block, or <see cref=\"Unknown\"/> if not found.</returns>");
    sb.AppendLine("    public static UnicodeBlock GetBlock(Rune rune) => GetBlock(rune.Value);");

    var result = $$"""
    // <auto-generated />
    // Generated from Unicode Block data
    // Data source: {{UcdUrls.Blocks}}
    // Statistics:
    //   - Total blocks: {{blockRanges.Count.ToString(CultureInfo.InvariantCulture)}}
    //   - Total code points in blocks: {{totalCodePoints.ToString("N0", CultureInfo.InvariantCulture)}}
    //
    // Specification: https://www.unicode.org/reports/tr44/#Blocks.txt
    // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

    #nullable enable

    namespace Meziantou.Framework;

    /// <summary>Provides access to all Unicode blocks.</summary>
    /// <remarks>
    /// Unicode blocks are named ranges of consecutive code points.
    /// For more information, see: https://www.unicode.org/Public/UCD/latest/ucd/Blocks.txt
    /// </remarks>
    public static class UnicodeBlocks
    {
        /// <summary>Unknown or unassigned block.</summary>
        public static UnicodeBlock Unknown { get; } = UnicodeBlock.CreateInternal("Unknown", new UnicodeRange(0, 0));

    {{sb.ToString().TrimEnd('\n')}}
    }
    """.ReplaceLineEndings("\n");

    if (await WriteTextIfChanged(unicodeBlocksFilePath, result))
        outputUpdated = true;

    static string ToPropertyName(string blockName)
    {
        // Convert block name to property name by removing all non-alphanumeric characters
        var sb = new StringBuilder();
        foreach (var c in blockName)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }

        var propertyName = sb.ToString();
        if (propertyName.Length == 0 || !char.IsLetter(propertyName[0]))
            throw new InvalidOperationException($"Block name '{blockName}' does not yield a valid C# identifier");

        return propertyName;
    }
}

internal sealed record Entry(Rune Source, string Target);

internal sealed record ScriptRange(int Start, int End, string Script);

internal sealed record ScriptExtensionRange(int Start, int End, List<int> Scripts);

internal sealed record PendingRange(int Start, UnicodeDataEntry Entry);

internal sealed record UnicodeDataEntry(
    Rune Rune,
    string Name,
    UnicodeCategory Category,
    UnicodeBidirectionalCategory BidiCategory,
    byte CanonicalCombiningClass,
    string? DecompositionMapping,
    sbyte DecimalDigitValue,
    sbyte DigitValue,
    string? NumericValue,
    bool Mirrored,
    string? Unicode1Name,
    string? IsoComment,
    int SimpleUppercaseMapping,
    int SimpleLowercaseMapping,
    int SimpleTitlecaseMapping,
    EmojiProperties EmojiProperties);

internal sealed record UnicodeDataChangedEntry(
    UnicodeDataEntry Previous,
    UnicodeDataEntry Current,
    IReadOnlyList<string> Differences);

internal sealed record UnicodeDataDiff(
    List<UnicodeDataEntry> Added,
    List<UnicodeDataEntry> Removed,
    List<UnicodeDataChangedEntry> Changed)
{
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Changed.Count > 0;
}
