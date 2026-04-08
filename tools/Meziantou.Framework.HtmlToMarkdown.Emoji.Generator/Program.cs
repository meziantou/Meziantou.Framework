#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meziantou.Framework;

const string GitHubEmojisUrl = "https://api.github.com/emojis";
const string UnicodeEmojiTestUrl = "https://www.unicode.org/Public/emoji/latest/emoji-test.txt";

if (!FullPath.CurrentDirectory().TryFindGitRepositoryRoot(out var root))
    throw new InvalidOperationException("Cannot find git root from " + FullPath.CurrentDirectory());

var outputFilePath = root / "src" / "Meziantou.Framework.HtmlToMarkdown" / "EmojiShortcodeMappings.g.cs";

var githubMappings = await LoadGitHubMappings();
var unicodeMappings = await LoadUnicodeMappings();

var output = GenerateCode(githubMappings, unicodeMappings);
if (await WriteTextIfChanged(outputFilePath, output))
{
    Console.WriteLine("The file has been updated");
    return 1;
}

return 0;

static async Task<Dictionary<string, string>> LoadGitHubMappings()
{
    using var request = new HttpRequestMessage(HttpMethod.Get, GitHubEmojisUrl);
    request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Meziantou.Framework.HtmlToMarkdown.Emoji.Generator", "1.0"));

    using var response = await SharedHttpClient.Instance.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadAsStringAsync();
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(payload)
        ?? throw new InvalidOperationException("Failed to deserialize GitHub emojis mapping");

    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var keyValuePair in data.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
    {
        var shortcode = ":" + keyValuePair.Key + ":";
        if (!TryParseGitHubEmojiFromUrl(keyValuePair.Value, out var emoji))
            continue;

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

static async Task<Dictionary<string, string>> LoadUnicodeMappings()
{
    using var response = await SharedHttpClient.Instance.GetAsync(UnicodeEmojiTestUrl);
    response.EnsureSuccessStatusCode();

    var content = await response.Content.ReadAsStringAsync();
    var candidates = new Dictionary<string, (string Shortcode, int Priority)>(StringComparer.Ordinal);
    foreach (var rawLine in content.Split('\n'))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
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
        if (metadataParts.Length < 3 || !metadataParts[1].StartsWith('E'))
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
        if (scalar is >= 0x20 and <= 0x7E && scalar is not '"' and not '\\')
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
