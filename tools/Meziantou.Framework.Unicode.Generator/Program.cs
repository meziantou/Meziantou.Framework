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
var csprojPath = root / "src" / "Meziantou.Framework.Unicode" / "Meziantou.Framework.Unicode.csproj";
var frameworkCsprojPath = root / "src" / "Meziantou.Framework" / "Meziantou.Framework.csproj";

var (confusableEntries, confusablesLastModified) = await LoadConfusablesEntries();
await WriteConfusableCharactersFile(confusableEntries, confusablesLastModified);
var blockRanges = await LoadBlocksRanges();
await WriteUnicodeBlocksFile(blockRanges);
await WriteUnicodeCharacterInfosFile();
await WriteUnicodeScriptsFile();
if (await PropertyTablesWriter.WriteAsync(outputPath))
    outputUpdated = true;

if (await BinaryPropertiesWriter.WriteAsync(outputPath))
    outputUpdated = true;

if (await EmojiSequencesWriter.WriteAsync(outputPath))
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

async Task WriteUnicodeCharacterInfosFile()
{
    var emojiProperties = await LoadEmojiProperties();
    var unicodeDataEntries = await LoadUnicodeDataEntries(emojiProperties);
    var content = UnicodeCharacterInfosWriter.Generate(unicodeDataEntries, UcdUrls.UnicodeData);
    if (File.Exists(unicodeCharacterInfosFilePath))
    {
        var existing = await File.ReadAllTextAsync(unicodeCharacterInfosFilePath);
        if (!string.Equals(existing.ReplaceLineEndings("\n"), content.ReplaceLineEndings("\n"), StringComparison.Ordinal))
        {
            var existingEntries = UnicodeCharacterInfosWriter.Read(existing);
            if (existingEntries is null)
            {
                Console.WriteLine("The existing Unicode data tables use a different layout. Rewriting them.");
            }
            else
            {
                var diff = DiffUnicodeData(existingEntries, unicodeDataEntries);
                if (diff.HasChanges)
                {
                    WriteUnicodeDataDiff(diff);
                }
                else
                {
                    Console.WriteLine("The Unicode data tables changed but no semantic changes were detected.");
                }
            }
        }
    }

    if (await WriteTextIfChanged(unicodeCharacterInfosFilePath, content))
        outputUpdated = true;
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

    // The names of the values of the Script property: the long name, the short name and the other aliases
    var aliasArms = new StringBuilder();
    foreach (var (longName, names) in await LoadScriptAliases())
    {
        if (!scriptIds.ContainsKey(longName))
            throw new InvalidOperationException("The script alias is not a script: " + longName);

        var patterns = string.Join(" or ", names.Select(name => "\"" + EscapeStringLiteral(name) + "\""));
        aliasArms.Append(CultureInfo.InvariantCulture, $"            {patterns} => UnicodeScript.{ToScriptIdentifier(longName)},\n");
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

            /// <summary>Gets the script whose name or alias (PropertyValueAliases.txt) is the value, compared exactly.</summary>
            private static UnicodeScript? ParseScriptCore(ReadOnlySpan<char> name)
            {
                return name switch
                {
        {{aliasArms.ToString().TrimEnd()}}
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

async Task<List<(string LongName, List<string> Names)>> LoadScriptAliases()
{
    var result = new List<(string LongName, List<string> Names)>();
    var content = await Ucd.DownloadAsync("ucd/PropertyValueAliases.txt");
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (commentIndex >= 0)
        {
            line = line[..commentIndex].Trim();
        }

        if (!line.StartsWith("sc ;", StringComparison.Ordinal))
            continue;

        // sc ; short name ; long name ; other aliases
        var parts = line.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            continue;

        List<string> names = [parts[2], parts[1], .. parts[3..]];
        result.Add((parts[2], names.Distinct(StringComparer.Ordinal).ToList()));
    }

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
    Console.WriteLine("Unicode data diff (semantic):");
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
