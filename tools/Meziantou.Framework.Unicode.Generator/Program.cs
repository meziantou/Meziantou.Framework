#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Buffers.Binary;
using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework;
using Meziantou.Framework.Versioning;

const string ConfusablesUrl = "https://www.unicode.org/Public/UCD/latest/security/confusables.txt";
const string UnicodeDataUrl = "https://www.unicode.org/Public/UCD/latest/ucd/UnicodeData.txt";
const string BlocksUrl = "https://www.unicode.org/Public/UCD/latest/ucd/Blocks.txt";
const string EmojiDataUrl = "https://www.unicode.org/Public/UCD/latest/ucd/emoji/emoji-data.txt";

var updated = false;
if (!FullPath.CurrentDirectory().TryFindFirstAncestorOrSelf(path => Directory.Exists(path / ".git"), out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var outputPath = root / "src" / "Meziantou.Framework.Unicode";
var confusableOutputFilePath = outputPath / "Unicode.Confusables.g.cs";
var unicodeCharacterInfosFilePath = outputPath / "UnicodeCharacterInfos.g.cs";
var unicodeBlocksFilePath = outputPath / "UnicodeBlocks.g.cs";
var emojiDataFilePath = outputPath / "UnicodeEmoji.g.cs";
var unicodeDataPath = outputPath / "Resources" / "UnicodeData.bin.gz";
var csprojPath = root / "src" / "Meziantou.Framework.Unicode" / "Meziantou.Framework.Unicode.csproj";

var (confusableEntries, confusablesLastModified) = await LoadConfusablesEntries();
await WriteConfusableCharactersFile(confusableEntries, confusablesLastModified);
var blockRanges = await LoadBlocksRanges();
await WriteUnicodeBlocksFile(blockRanges);
await WriteUnicodeCharacterInfosFile(blockRanges);
await WriteEmojiDataFile();

// Update project version
if (updated)
{
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    var doc = XDocument.Load(csprojPath, LoadOptions.PreserveWhitespace);
    var versionNode = doc.Descendants().First(e => e.Name.LocalName == "Version");
    var version = SemanticVersion.Parse(versionNode.Value);
    versionNode.Value = version.NextPatchVersion().ToString();

    var xws = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, Encoding = encoding, Async = true, };
    await using var writer = XmlWriter.Create(csprojPath, xws);
    await doc.SaveAsync(writer, CancellationToken.None);
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
        var output = await gitProcess.StandardOutput.ReadToEndAsync();
        var error = await gitProcess.StandardError.ReadToEndAsync();
        await gitProcess.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(output))
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

return 0;

static async Task<(List<Entry> entries, string lastModified)> LoadConfusablesEntries()
{
    Console.WriteLine($"  Downloading from: {ConfusablesUrl}");
    using var response = await SharedHttpClient.Instance.GetAsync(ConfusablesUrl);
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
        if (line.Length == 0 || line.StartsWith('#'))
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
    Console.WriteLine($"  Downloading from: {UnicodeDataUrl}");
    using var response = await SharedHttpClient.Instance.GetAsync(UnicodeDataUrl);
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
    Console.WriteLine($"  Downloading from: {BlocksUrl}");
    using var response = await SharedHttpClient.Instance.GetAsync(BlocksUrl);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var blockRanges = new List<(int Start, int End, string Name)>();
    var lineCount = 0;

    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
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
    Console.WriteLine($"  Downloading from: {EmojiDataUrl}");
    using var response = await SharedHttpClient.Instance.GetAsync(EmojiDataUrl);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var emojiData = new Dictionary<int, EmojiProperties>();
    var lineCount = 0;
    var processedLines = 0;

    foreach (var rawLine in content.Split('\n'))
    {
        lineCount++;
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
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
    if (WriteBinaryIfChanged(unicodeDataPath, unicodeDataBytes))
        updated = true;

    var maxNameLength = unicodeDataEntries.Max(entry => entry.Name.Length);
    var maxNameLengthPowerOfTwo = NextPowerOfTwo(maxNameLength);

    if (maxNameLengthPowerOfTwo > 256)
        throw new InvalidOperationException("Max character name length exceeds 256: " + maxNameLengthPowerOfTwo);

    var emojiCount = unicodeDataEntries.Count(e => e.EmojiProperties != EmojiProperties.None);
    var compressedSize = unicodeDataBytes.Length;
    var compressionRatio = unicodeDataEntries.Count > 0 ? (double)compressedSize / (unicodeDataEntries.Count * 50) : 0;

    var content = string.Create(CultureInfo.InvariantCulture, $$"""
        // <auto-generated />
        // Generated from Unicode Character Database
        // Data source: {{UnicodeDataUrl}}
        // Binary resource: UnicodeData.bin.gz
        // Statistics:
        //   - Total characters: {{unicodeDataEntries.Count}}
        //   - Emoji characters: {{emojiCount}}
        //   - Compressed size: {{compressedSize:N0}} bytes ({{compressionRatio * 100d:F1}}% of uncompressed)
        //   - Max character name length: {{maxNameLength}} chars
        // 
        // Specification: https://www.unicode.org/reports/tr44/
        // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool
        
        namespace Meziantou.Framework;
        internal static partial class UnicodeCharacterInfos
        {
            private const int MaxCharacterNameLength = {{maxNameLengthPowerOfTwo}};
        }
        """);

    if (await WriteTextIfChanged(unicodeCharacterInfosFilePath, content))
    {
        updated = true;
    }
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

static Rune ParseSingleRune(string input)
{
    var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length != 1)
        throw new InvalidOperationException("Source is not a single rune: " + input);

    var value = int.Parse(tokens[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    return new Rune(value);
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
    var stringIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    var strings = new List<string>();
    var serialized = new List<SerializedUnicodeDataEntry>(entries.Count);

    // First pass: collect all unique strings
    foreach (var entry in entries)
    {
        CollectString(entry.Name);
        CollectString(entry.DecompositionMapping);
        CollectString(entry.NumericValue);
        CollectString(entry.Unicode1Name);
        CollectString(entry.IsoComment);
    }

    // Sort strings for stable binary output
    strings.Sort(StringComparer.Ordinal);

    // Rebuild string index with sorted positions
    stringIndex.Clear();
    for (var i = 0; i < strings.Count; i++)
    {
        stringIndex[strings[i]] = i;
    }

    // Second pass: create serialized entries with correct sorted indices
    foreach (var entry in entries)
    {
        serialized.Add(new SerializedUnicodeDataEntry(
            entry.Rune.Value,
            GetStringIndex(entry.Name),
            entry.Category,
            entry.BidiCategory,
            entry.CanonicalCombiningClass,
            GetStringIndex(entry.DecompositionMapping),
            entry.DecimalDigitValue,
            entry.DigitValue,
            GetStringIndex(entry.NumericValue),
            entry.Mirrored,
            GetStringIndex(entry.Unicode1Name),
            GetStringIndex(entry.IsoComment),
            entry.SimpleUppercaseMapping,
            entry.SimpleLowercaseMapping,
            entry.SimpleTitlecaseMapping,
            entry.EmojiProperties));
    }

    var maxStringLength = strings.Count == 0 ? 0 : strings.Max(s => s.Length);
    if (maxStringLength > 255)
        throw new InvalidOperationException("A string exceeds the maximum length of 255 characters.");

    if (strings.Count > 65536 - 1)
        throw new InvalidOperationException("Too many strings: " + strings.Count);

    Console.WriteLine($"Serialized {serialized.Count} UnicodeData entries with {strings.Count} unique strings (max length: {maxStringLength})");

    using (var compressed = new MemoryStream())
    {
        using (var stream = new System.IO.Compression.GZipStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            Write7BitEncodedInt(stream, serialized.Count);
            Write7BitEncodedInt(stream, strings.Count);

            foreach (var value in strings)
            {
                WriteUtf8StringMax255Length(stream, value);
            }

            foreach (var entry in serialized.OrderBy(entry => entry.RuneValue))
            {
                WriteInt32(stream, entry.RuneValue);
                WriteStringIndex(stream, entry.NameIndex);
                WriteByte(stream, (byte)entry.Category);
                WriteByte(stream, (byte)entry.BidiCategory);
                WriteByte(stream, entry.CanonicalCombiningClass);
                WriteStringIndex(stream, entry.DecompositionIndex);
                WriteSByte(stream, entry.DecimalDigitValue);
                WriteSByte(stream, entry.DigitValue);
                WriteStringIndex(stream, entry.NumericIndex);
                WriteByte(stream, entry.Mirrored ? (byte)1 : (byte)0);
                WriteStringIndex(stream, entry.Unicode1NameIndex);
                WriteStringIndex(stream, entry.IsoCommentIndex);
                WriteInt32(stream, entry.SimpleUppercaseMapping);
                WriteInt32(stream, entry.SimpleLowercaseMapping);
                WriteInt32(stream, entry.SimpleTitlecaseMapping);
                WriteByte(stream, (byte)entry.EmojiProperties);
            }
        }

        return compressed.ToArray();
    }

    void CollectString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (!stringIndex.ContainsKey(value))
        {
            stringIndex.Add(value, strings.Count);
            strings.Add(value);
        }
    }

    int GetStringIndex(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return -1;

        return stringIndex[value];
    }
}

static bool WriteBinaryIfChanged(FullPath filePath, byte[] content)
{
    if (File.Exists(filePath))
    {
        var existing = File.ReadAllBytes(filePath);
        if (existing.AsSpan().SequenceEqual(content))
            return false;
    }

    filePath.CreateParentDirectory();
    File.WriteAllBytes(filePath, content);
    return true;
}

static async Task<bool> WriteTextIfChanged(FullPath filePath, string content)
{
    if (File.Exists(filePath) && (await File.ReadAllTextAsync(filePath)).ReplaceLineEndings("\n") == content)
        return false;

    filePath.CreateParentDirectory();
    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    await File.WriteAllTextAsync(filePath, content, encoding);
    return true;
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

static UnicodeCategory ParseUnicodeCategory(string value)
{
    return value switch
    {
        "Lu" => UnicodeCategory.UppercaseLetter,
        "Ll" => UnicodeCategory.LowercaseLetter,
        "Lt" => UnicodeCategory.TitlecaseLetter,
        "Lm" => UnicodeCategory.ModifierLetter,
        "Lo" => UnicodeCategory.OtherLetter,
        "Mn" => UnicodeCategory.NonSpacingMark,
        "Mc" => UnicodeCategory.SpacingCombiningMark,
        "Me" => UnicodeCategory.EnclosingMark,
        "Nd" => UnicodeCategory.DecimalDigitNumber,
        "Nl" => UnicodeCategory.LetterNumber,
        "No" => UnicodeCategory.OtherNumber,
        "Pc" => UnicodeCategory.ConnectorPunctuation,
        "Pd" => UnicodeCategory.DashPunctuation,
        "Ps" => UnicodeCategory.OpenPunctuation,
        "Pe" => UnicodeCategory.ClosePunctuation,
        "Pi" => UnicodeCategory.InitialQuotePunctuation,
        "Pf" => UnicodeCategory.FinalQuotePunctuation,
        "Po" => UnicodeCategory.OtherPunctuation,
        "Sm" => UnicodeCategory.MathSymbol,
        "Sc" => UnicodeCategory.CurrencySymbol,
        "Sk" => UnicodeCategory.ModifierSymbol,
        "So" => UnicodeCategory.OtherSymbol,
        "Zs" => UnicodeCategory.SpaceSeparator,
        "Zl" => UnicodeCategory.LineSeparator,
        "Zp" => UnicodeCategory.ParagraphSeparator,
        "Cc" => UnicodeCategory.Control,
        "Cf" => UnicodeCategory.Format,
        "Cs" => UnicodeCategory.Surrogate,
        "Co" => UnicodeCategory.PrivateUse,
        "Cn" => UnicodeCategory.OtherNotAssigned,
        _ => throw new InvalidOperationException("Unknown Unicode category: " + value),
    };
}

static UnicodeBidirectionalCategory ParseBidirectionalCategory(string value)
{
    return value switch
    {
        "L" => UnicodeBidirectionalCategory.LeftToRight,
        "R" => UnicodeBidirectionalCategory.RightToLeft,
        "AL" => UnicodeBidirectionalCategory.RightToLeftArabic,
        "EN" => UnicodeBidirectionalCategory.EuropeanNumber,
        "ES" => UnicodeBidirectionalCategory.EuropeanSeparator,
        "ET" => UnicodeBidirectionalCategory.EuropeanTerminator,
        "AN" => UnicodeBidirectionalCategory.ArabicNumber,
        "CS" => UnicodeBidirectionalCategory.CommonSeparator,
        "B" => UnicodeBidirectionalCategory.ParagraphSeparator,
        "S" => UnicodeBidirectionalCategory.SegmentSeparator,
        "WS" => UnicodeBidirectionalCategory.WhiteSpace,
        "ON" => UnicodeBidirectionalCategory.OtherNeutral,
        "NSM" => UnicodeBidirectionalCategory.NonspacingMark,
        "LRE" => UnicodeBidirectionalCategory.LeftToRightEmbedding,
        "LRO" => UnicodeBidirectionalCategory.LeftToRightOverride,
        "RLE" => UnicodeBidirectionalCategory.RightToLeftEmbedding,
        "RLO" => UnicodeBidirectionalCategory.RightToLeftOverride,
        "PDF" => UnicodeBidirectionalCategory.PopDirectionalFormat,
        "LRI" => UnicodeBidirectionalCategory.LeftToRightIsolate,
        "RLI" => UnicodeBidirectionalCategory.RightToLeftIsolate,
        "FSI" => UnicodeBidirectionalCategory.FirstStrongIsolate,
        "PDI" => UnicodeBidirectionalCategory.PopDirectionalIsolate,
        "BN" => UnicodeBidirectionalCategory.BoundaryNeutral,
        _ => throw new InvalidOperationException("Unknown bidirectional category: " + value),
    };
}

static void WriteUtf8StringMax255Length(Stream stream, string value)
{
    var bytes = Encoding.UTF8.GetBytes(value);
    if (bytes.Length > 255)
        throw new InvalidOperationException("String exceeds maximum length of 255 bytes.");

    stream.WriteByte((byte)bytes.Length);
    stream.Write(bytes, 0, bytes.Length);
}

static void WriteStringIndex(Stream stream, int value)
{
    var persistedValue = (ushort)(value + 1);

    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buffer, persistedValue);
    stream.Write(buffer);
}

static void WriteInt32(Stream stream, int value)
{
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
    stream.Write(buffer);
}

static void Write7BitEncodedInt(Stream stream, int value)
{
    var uValue = (uint)value;
    while (uValue >= 0x80)
    {
        stream.WriteByte((byte)(uValue | 0x80));
        uValue >>= 7;
    }

    stream.WriteByte((byte)uValue);
}

static void WriteSByte(Stream stream, sbyte value)
{
    stream.WriteByte(unchecked((byte)value));
}

static void WriteByte(Stream stream, byte value)
{
    stream.WriteByte(value);
}

async Task WriteEmojiDataFile()
{
    var emojiProperties = await LoadEmojiProperties();
    var emojiCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.Emoji));
    var emojiPresentationCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.EmojiPresentation));
    var modifierCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.EmojiModifier));
    var modifierBaseCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.EmojiModifierBase));
    var componentCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.EmojiComponent));
    var pictographicCount = emojiProperties.Count(kvp => kvp.Value.HasFlag(EmojiProperties.ExtendedPictographic));

    var result = $$"""
    // <auto-generated />
    // Generated from Unicode Emoji data
    // Data source: {{EmojiDataUrl}}
    // Statistics:
    //   - Emoji: {{emojiCount}} code points
    //   - Emoji_Presentation: {{emojiPresentationCount}} code points
    //   - Emoji_Modifier: {{modifierCount}} code points
    //   - Emoji_Modifier_Base: {{modifierBaseCount}} code points
    //   - Emoji_Component: {{componentCount}} code points
    //   - Extended_Pictographic: {{pictographicCount}} code points
    // 
    // Specification: https://www.unicode.org/reports/tr51/
    // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool
    
    #nullable enable
    
    using System.Text;
    
    namespace Meziantou.Framework;
    
    /// <summary>Provides emoji-related query methods for Unicode characters.</summary>
    /// <remarks>
    /// This class provides methods to check various emoji properties defined in Unicode Technical Standard #51.
    /// For more information, see: https://www.unicode.org/reports/tr51/
    /// </remarks>
    public static class UnicodeEmoji
    {
        /// <summary>Determines whether the specified rune has the Emoji property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Emoji property; otherwise, <see langword="false"/>.</returns>
        public static bool IsEmoji(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.IsEmoji;
        }
    
        /// <summary>Determines whether the specified code point has the Emoji property.</summary>
        /// <param name="codePoint">The code point to check.</param>
        /// <returns><see langword="true"/> if the code point has the Emoji property; otherwise, <see langword="false"/>.</returns>
        public static bool IsEmoji(int codePoint)
        {
            return Rune.TryCreate(codePoint, out var rune) && IsEmoji(rune);
        }
    
        /// <summary>Determines whether the specified rune has the Emoji_Presentation property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Emoji_Presentation property; otherwise, <see langword="false"/>.</returns>
        public static bool HasEmojiPresentation(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.HasEmojiPresentation;
        }
    
        /// <summary>Determines whether the specified rune has the Emoji_Modifier property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Emoji_Modifier property; otherwise, <see langword="false"/>.</returns>
        public static bool IsEmojiModifier(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.IsEmojiModifier;
        }
    
        /// <summary>Determines whether the specified rune has the Emoji_Modifier_Base property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Emoji_Modifier_Base property; otherwise, <see langword="false"/>.</returns>
        public static bool IsEmojiModifierBase(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.IsEmojiModifierBase;
        }
    
        /// <summary>Determines whether the specified rune has the Emoji_Component property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Emoji_Component property; otherwise, <see langword="false"/>.</returns>
        public static bool IsEmojiComponent(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.IsEmojiComponent;
        }
    
        /// <summary>Determines whether the specified rune has the Extended_Pictographic property.</summary>
        /// <param name="rune">The rune to check.</param>
        /// <returns><see langword="true"/> if the rune has the Extended_Pictographic property; otherwise, <see langword="false"/>.</returns>
        public static bool IsExtendedPictographic(Rune rune)
        {
            if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
                return false;
    
            return info.IsExtendedPictographic;
        }
    }
    """.ReplaceLineEndings("\n");

    if (await WriteTextIfChanged(emojiDataFilePath, result))
    {
        updated = true;
    }
}

async Task WriteConfusableCharactersFile(List<Entry> confusableEntries, string confusablesLastModified)
{
    var sb = new StringBuilder();
    foreach (var entry in confusableEntries.OrderBy(entry => entry.Source.Value))
    {
        sb.Append("            [new Rune(0x");
        sb.Append(entry.Source.Value.ToString("X", CultureInfo.InvariantCulture));
        sb.Append(")] = \"");
        sb.Append(EscapeString(entry.Target));
        sb.Append("\",\n");
    }

    var multiCharMappings = confusableEntries.Count(e => e.Target.EnumerateRunes().Count() > 1);
    var singleCharMappings = confusableEntries.Count - multiCharMappings;

    var result = $$"""
    // <auto-generated />
    // Generated from Unicode Security Confusables data
    // Data source: {{ConfusablesUrl}}
    // Last modified: {{confusablesLastModified}}
    // Statistics:
    //   - Total confusable mappings: {{confusableEntries.Count.ToString(CultureInfo.InvariantCulture)}}
    //   - Single-character replacements: {{singleCharMappings.ToString(CultureInfo.InvariantCulture)}}
    //   - Multi-character replacements: {{multiCharMappings.ToString(CultureInfo.InvariantCulture)}}
    // 
    // Specification: https://www.unicode.org/reports/tr39/
    // Purpose: Helps detect visually confusable characters that may be used in security attacks
    // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

    #nullable enable

    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    namespace Meziantou.Framework;

    internal static class UnicodeConfusablesData
    {
        private static readonly Dictionary<Rune, string> Confusables = Create();

        public static bool TryGetReplacement(Rune rune, out string? replacement)
        {
            return Confusables.TryGetValue(rune, out replacement);
        }

        private static Dictionary<Rune, string> Create()
        {
            return new Dictionary<Rune, string>(capacity: {{confusableEntries.Count}})
            {
    {{sb.ToString().TrimEnd('\n')}}
            };
        }
    }
    """.ReplaceLineEndings("\n");

    if (await WriteTextIfChanged(confusableOutputFilePath, result))
    {
        updated = true;
    }
}

async Task WriteUnicodeBlocksFile(List<(int Start, int End, string Name)> blockRanges)
{
    var sb = new StringBuilder();

    // Generate property for each block
    foreach (var (start, end, name) in blockRanges.OrderBy(b => b.Start))
    {
        var propertyName = ToPropertyName(name);
        var codePointCount = end - start + 1;
        sb.AppendLine(CultureInfo.InvariantCulture, $"    /// <summary>{name} (U+{start:X4}..U+{end:X4}, {codePointCount:N0} code points).</summary>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    public static UnicodeBlock {propertyName} {{ get; }} = UnicodeBlock.CreateInternal(\"{name}\", new UnicodeRange(0x{start:X}, 0x{end:X}));");
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
    // Data source: {{BlocksUrl}}
    // Statistics:
    //   - Total blocks: {{blockRanges.Count.ToString(CultureInfo.InvariantCulture)}}
    //   - Total code points in blocks: {{totalCodePoints.ToString("N0", CultureInfo.InvariantCulture)}}
    // 
    // Specification: https://www.unicode.org/reports/tr44/#Blocks.txt
    // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool
    
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
    {
        updated = true;
    }

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
        return sb.ToString();
    }
}

internal sealed record Entry(Rune Source, string Target);

internal sealed record PendingRange(int Start, UnicodeDataEntry Entry);

[Flags]
internal enum EmojiProperties : byte
{
    None = 0,
    Emoji = 1 << 0,
    EmojiPresentation = 1 << 1,
    EmojiModifier = 1 << 2,
    EmojiModifierBase = 1 << 3,
    EmojiComponent = 1 << 4,
    ExtendedPictographic = 1 << 5,
}

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

internal sealed record SerializedUnicodeDataEntry(
int RuneValue,
int NameIndex,
UnicodeCategory Category,
UnicodeBidirectionalCategory BidiCategory,
byte CanonicalCombiningClass,
int DecompositionIndex,
sbyte DecimalDigitValue,
sbyte DigitValue,
int NumericIndex,
bool Mirrored,
int Unicode1NameIndex,
int IsoCommentIndex,
int SimpleUppercaseMapping,
int SimpleLowercaseMapping,
int SimpleTitlecaseMapping,
EmojiProperties EmojiProperties);
