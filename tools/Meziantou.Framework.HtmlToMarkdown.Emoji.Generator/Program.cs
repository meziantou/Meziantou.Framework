#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Net.Http.Headers;
using System.Text.Json;
using Meziantou.Framework;

const string GitHubEmojisUrl = "https://api.github.com/emojis";
const string UnicodeEmojiTestUrl = "https://www.unicode.org/Public/emoji/latest/emoji-test.txt";

if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var outputFilePath = root / "src" / "Meziantou.Framework.HtmlToMarkdown" / "EmojiShortcodeMappings.g.cs";

// The shortcodes that the emoji extension of Meziantou.Framework.Markdown reads: every shortcode of GitHub, aliases included
var markdownOutputFilePath = root / "src" / "Meziantou.Framework.Markdown" / "Parsing" / "EmojiShortcodes.g.cs";

var githubShortcodes = await LoadGitHubShortcodes();
var githubMappings = GetEmojiToShortcode(githubShortcodes);
var emojiTest = await DownloadEmojiTest();
var unicodeMappings = LoadUnicodeMappings(emojiTest);

var updated = await WriteTextIfChanged(outputFilePath, GenerateCode(githubMappings, unicodeMappings));
var markdownShortcodes = ToFullyQualified(githubShortcodes, emojiTest);
updated |= await WriteTextIfChanged(markdownOutputFilePath, GenerateMarkdownCode(markdownShortcodes, ResolveSmileys(markdownShortcodes, emojiTest)));
if (updated)
{
    Console.WriteLine("The file has been updated");
    return 1;
}

return 0;

// The shortcodes of GitHub that are Unicode emoji, by shortcode: the other ones are images, such as :octocat:
static async Task<SortedDictionary<string, string>> LoadGitHubShortcodes()
{
    using var request = new HttpRequestMessage(HttpMethod.Get, GitHubEmojisUrl);
    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.HtmlToMarkdown.Emoji.Generator", "1.0"));

    // Anonymous calls share the rate limit of the runner's IP address, which other jobs often exhaust
    var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (!string.IsNullOrEmpty(token))
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    using var response = await SharedHttpClient.Instance.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadAsStringAsync();
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(payload)
        ?? throw new InvalidOperationException("Failed to deserialize GitHub emojis mapping");

    var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var keyValuePair in data)
    {
        if (TryParseGitHubEmojiFromUrl(keyValuePair.Value, out var emoji))
        {
            result[":" + keyValuePair.Key + ":"] = emoji;
        }
    }

    return result;
}

// The first shortcode of each emoji, in the order of the names of the shortcodes
static Dictionary<string, string> GetEmojiToShortcode(SortedDictionary<string, string> shortcodes)
{
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (shortcode, emoji) in shortcodes.OrderBy(static entry => entry.Key.Trim(':'), StringComparer.Ordinal))
    {
        if (result.TryGetValue(emoji, out var existingShortcode))
        {
            if (string.CompareOrdinal(shortcode, existingShortcode) < 0)
            {
                result[emoji] = shortcode;
            }
        }
        else
        {
            result[emoji] = shortcode;
        }
    }

    return result;
}

static async Task<string> DownloadEmojiTest()
{
    using var response = await SharedHttpClient.Instance.GetAsync(UnicodeEmojiTestUrl);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsStringAsync();
}

// The names of the images of GitHub leave out the zero width joiners and the variation selectors of the emoji: the emoji is
// the fully-qualified sequence of emoji-test.txt that has the same other code points
static SortedDictionary<string, string> ToFullyQualified(SortedDictionary<string, string> shortcodes, string emojiTest)
{
    var fullyQualified = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var rawLine in emojiTest.Split('\n'))
    {
        var line = rawLine.Trim();
        var semicolonIndex = line.IndexOf(';', StringComparison.Ordinal);
        var hashIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (line.StartsWith('#', StringComparison.Ordinal) || semicolonIndex <= 0 || hashIndex <= semicolonIndex || line[(semicolonIndex + 1)..hashIndex].Trim() != "fully-qualified")
            continue;

        if (TryParseCodePointSequence(line[..semicolonIndex].Trim(), out var emoji))
        {
            fullyQualified.TryAdd(RemoveJoinersAndSelectors(emoji), emoji);
        }
    }

    var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var (shortcode, emoji) in shortcodes)
    {
        result[shortcode] = fullyQualified.GetValueOrDefault(RemoveJoinersAndSelectors(emoji)) ?? emoji;
    }

    return result;

    static string RemoveJoinersAndSelectors(string emoji) => emoji.Replace("\u200D", "", StringComparison.Ordinal).Replace("\uFE0F", "", StringComparison.Ordinal);
}

// The shortcode of the emoji of each smiley: the emoji is the fully-qualified sequence of its name in emoji-test.txt, and the
// shortcode the first one of GitHub for it
static SortedDictionary<string, string> ResolveSmileys(SortedDictionary<string, string> shortcodes, string emojiTest)
{
    var emojiByName = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var rawLine in emojiTest.Split('\n'))
    {
        var line = rawLine.Trim();
        var semicolonIndex = line.IndexOf(';', StringComparison.Ordinal);
        var hashIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (line.StartsWith('#', StringComparison.Ordinal) || semicolonIndex <= 0 || hashIndex <= semicolonIndex || line[(semicolonIndex + 1)..hashIndex].Trim() != "fully-qualified")
            continue;

        // # 😃 E0.6 grinning face with big eyes
        var metadata = line[(hashIndex + 1)..].Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (metadata.Length == 3 && TryParseCodePointSequence(line[..semicolonIndex].Trim(), out var emoji))
        {
            emojiByName.TryAdd(metadata[2], emoji);
        }
    }

    var shortcodeByEmoji = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (shortcode, emoji) in shortcodes)
    {
        shortcodeByEmoji.TryAdd(emoji, shortcode);
    }

    var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var (smiley, emojiName) in Smileys.All)
    {
        if (!emojiByName.TryGetValue(emojiName, out var emoji))
            throw new InvalidOperationException($"The emoji '{emojiName}' of the smiley '{smiley}' is not in emoji-test.txt");

        if (!shortcodeByEmoji.TryGetValue(emoji, out var shortcode))
            throw new InvalidOperationException($"The emoji '{emojiName}' of the smiley '{smiley}' has no shortcode on GitHub");

        if (!result.TryAdd(smiley, shortcode))
            throw new InvalidOperationException($"The smiley '{smiley}' is defined twice");
    }

    return result;
}

static Dictionary<string, string> LoadUnicodeMappings(string content)
{
    var candidates = new Dictionary<string, (string Shortcode, int Priority)>(StringComparer.Ordinal);
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#', StringComparison.Ordinal))
            continue;

        var semicolonIndex = line.IndexOf(';', StringComparison.Ordinal);
        if (semicolonIndex <= 0)
            continue;

        var hashIndex = line.IndexOf('#', StringComparison.Ordinal);
        if (hashIndex <= semicolonIndex)
            continue;

        var codePointSequence = line[..semicolonIndex].Trim();
        var status = line[(semicolonIndex + 1)..hashIndex].Trim();
        if (!TryParseCodePointSequence(codePointSequence, out var emoji))
            continue;

        var metadata = line[(hashIndex + 1)..].Trim();
        var metadataParts = metadata.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (metadataParts.Length < 3 || !metadataParts[1].StartsWith('E', StringComparison.Ordinal))
            continue;

        var name = metadataParts[2].Trim();
        var normalizedName = NormalizeShortcodeName(name);
        if (normalizedName.Length == 0)
            continue;

        var shortcode = ":" + normalizedName + ":";
        var priority = GetStatusPriority(status);
        if (candidates.TryGetValue(emoji, out var existing) && existing.Priority <= priority)
            continue;

        candidates[emoji] = (shortcode, priority);
    }

    return candidates
        .OrderBy(static item => item.Key, StringComparer.Ordinal)
        .ToDictionary(static item => item.Key, static item => item.Value.Shortcode, StringComparer.Ordinal);
}

static bool TryParseGitHubEmojiFromUrl(string url, out string emoji)
{
    emoji = "";
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        return false;

    var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var unicodeIndex = Array.FindIndex(segments, static segment => string.Equals(segment, "unicode", StringComparison.OrdinalIgnoreCase));
    if (unicodeIndex < 0 || unicodeIndex >= segments.Length - 1)
        return false;

    var fileName = Path.GetFileNameWithoutExtension(segments[unicodeIndex + 1]);
    return fileName.Length > 0 && TryParseCodePointSequence(fileName.Replace('-', ' '), out emoji);
}

static bool TryParseCodePointSequence(string input, out string emoji)
{
    emoji = "";
    var sb = new StringBuilder();
    foreach (var token in input.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        if (!Rune.TryCreate(value, out var rune))
            return false;

        sb.Append(rune);
    }

    if (sb.Length == 0)
        return false;

    emoji = sb.ToString();
    return true;
}

static int GetStatusPriority(string status)
{
    return status switch
    {
        "fully-qualified" => 0,
        "minimally-qualified" => 1,
        "unqualified" => 2,
        "component" => 3,
        _ => int.MaxValue,
    };
}

static string NormalizeShortcodeName(string name)
{
    var sb = new StringBuilder(name.Length);
    var lastWasSeparator = false;
    foreach (var rune in name.ToLowerInvariant().EnumerateRunes())
    {
        if (Rune.IsLetterOrDigit(rune))
        {
            sb.Append(rune);
            lastWasSeparator = false;
            continue;
        }

        if (!lastWasSeparator)
        {
            sb.Append('_');
            lastWasSeparator = true;
        }
    }

    return sb.ToString().Trim('_');
}

static string GenerateCode(IReadOnlyDictionary<string, string> githubMappings, IReadOnlyDictionary<string, string> unicodeMappings)
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine();
    sb.AppendLine("namespace Meziantou.Framework;");
    sb.AppendLine();
    sb.AppendLine("internal static class EmojiShortcodeMappings");
    sb.AppendLine("{");
    AppendDictionary(sb, dictionaryName: "GitHub", sourceComment: GitHubEmojisUrl, githubMappings);
    sb.AppendLine();
    AppendDictionary(sb, dictionaryName: "Unicode", sourceComment: UnicodeEmojiTestUrl, unicodeMappings);
    sb.AppendLine("}");
    return sb.ToString();
}

static string GenerateMarkdownCode(SortedDictionary<string, string> shortcodes, SortedDictionary<string, string> smileys)
{
    var sb = new StringBuilder();
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine("namespace Meziantou.Framework.Markdown.Parsing;");
    sb.AppendLine();
    sb.AppendLine("static partial class EmojiData");
    sb.AppendLine("{");
    sb.AppendLine($"    // Source: {GitHubEmojisUrl}, the shortcodes that are Unicode emoji");
    sb.AppendLine("    private static Dictionary<string, string> GetGitHubShortcodes() => new(" + shortcodes.Count.ToString(CultureInfo.InvariantCulture) + ", StringComparer.Ordinal)");
    sb.AppendLine("    {");
    foreach (var (shortcode, emoji) in shortcodes)
    {
        sb.AppendLine($"        [\"{EscapeString(shortcode)}\"] = \"{EscapeString(emoji)}\",");
    }

    sb.AppendLine("    };");
    sb.AppendLine();
    sb.AppendLine($"    // Source: tools/Meziantou.Framework.HtmlToMarkdown.Emoji.Generator/Smileys.cs, whose emoji are resolved with {UnicodeEmojiTestUrl}");
    sb.AppendLine("    private static Dictionary<string, string> GetUnicodeSmileys() => new(" + smileys.Count.ToString(CultureInfo.InvariantCulture) + ", StringComparer.Ordinal)");
    sb.AppendLine("    {");
    foreach (var (smiley, shortcode) in smileys)
    {
        sb.AppendLine($"        [\"{EscapeString(smiley)}\"] = \"{EscapeString(shortcode)}\",");
    }

    sb.AppendLine("    };");
    sb.AppendLine("}");
    return sb.ToString();
}

static void AppendDictionary(StringBuilder sb, string dictionaryName, string sourceComment, IReadOnlyDictionary<string, string> dictionary)
{
    sb.AppendLine($"    // Source: {sourceComment}");
    sb.AppendLine($"    public static readonly IReadOnlyDictionary<string, string> {dictionaryName} = new Dictionary<string, string>(StringComparer.Ordinal)");
    sb.AppendLine("    {");
    foreach (var keyValuePair in dictionary)
    {
        sb.AppendLine($"        {{ \"{EscapeString(keyValuePair.Key)}\", \"{EscapeString(keyValuePair.Value)}\" }},");
    }

    sb.AppendLine("    };");
}

static string EscapeString(string value)
{
    var sb = new StringBuilder();
    foreach (var rune in value.EnumerateRunes())
    {
        var scalar = rune.Value;
        if (scalar is >= 0x20 and <= 0x7E and not (0x22 or 0x5C))
        {
            sb.Append((char)scalar);
        }
        else if (scalar <= 0xFFFF)
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

static async Task<bool> WriteTextIfChanged(FullPath filePath, string content)
{
    var normalizedContent = content.ReplaceLineEndings("\n");
    if (File.Exists(filePath))
    {
        var existingContent = await File.ReadAllTextAsync(filePath);
        if (existingContent.ReplaceLineEndings("\n") == normalizedContent)
            return false;
    }

    var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    await File.WriteAllTextAsync(filePath, normalizedContent, encoding);
    return true;
}
