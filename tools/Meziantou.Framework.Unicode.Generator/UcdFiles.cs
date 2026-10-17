#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework;

/// <summary>Downloads the files of one pinned version of the Unicode Character Database.</summary>
/// <remarks>
/// The version is pinned because the segmentation and bidirectional algorithms of the library are written for the
/// rules of one version of the standard: data of a newer version with the rules of an older one fails conformance.
/// </remarks>
internal static class Ucd
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static string? s_version;

    /// <summary>Gets the pinned version, such as <c>18.0.0</c>.</summary>
    public static string Version => s_version ?? throw new InvalidOperationException("The Unicode version is not loaded");

    /// <summary>Gets the path of the file that pins the version, relative to the repository root.</summary>
    public static FullPath GetVersionFilePath(FullPath root) => root / "tools" / "Meziantou.Framework.Unicode.Generator" / "unicode-version.txt";

    public static void LoadVersion(FullPath root)
    {
        var value = File.ReadAllText(GetVersionFilePath(root)).Trim();
        if (!System.Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
            throw new InvalidOperationException("Invalid Unicode version: " + value);

        s_version = value;
        Cache.Clear();
    }

    /// <summary>Gets the version published as the latest one, read from the header of its ReadMe.txt.</summary>
    public static async Task<string> GetLatestVersionAsync()
    {
        const string Url = "https://www.unicode.org/Public/UCD/latest/ucd/ReadMe.txt";
        using var response = await SharedHttpClient.Instance.GetAsync(Url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        const string Marker = "for version ";
        var index = content.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("Cannot find the version in " + Url);

        var start = index + Marker.Length;
        var end = start;
        while (end < content.Length && (char.IsAsciiDigit(content[end]) || content[end] == '.'))
        {
            end++;
        }

        var value = content[start..end].TrimEnd('.');
        if (!System.Version.TryParse(value, out var version) || version.Build < 0)
            throw new InvalidOperationException("Cannot parse the version in " + Url + ": " + value);

        return value;
    }

    /// <summary>Gets the URL of a file, such as <c>ucd/LineBreak.txt</c> or <c>security/confusables.txt</c>.</summary>
    public static string GetUrl(string relativePath) => "https://www.unicode.org/Public/" + Version + "/" + relativePath;

    public static async Task<HttpResponseMessage> GetAsync(string relativePath)
    {
        var url = GetUrl(relativePath);
        Console.WriteLine($"  Downloading from: {url}");
        var response = await SharedHttpClient.Instance.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return response;
    }

    public static async Task<byte[]> DownloadBytesAsync(string relativePath)
    {
        using var response = await GetAsync(relativePath);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public static async Task<string> DownloadAsync(string relativePath)
    {
        if (Cache.TryGetValue(relativePath, out var content))
            return content;

        using var response = await GetAsync(relativePath);
        content = await response.Content.ReadAsStringAsync();
        Cache[relativePath] = content;
        return content;
    }

    /// <summary>Parses a data file made of <c>XXXX[..YYYY] ; field ; field # comment</c> lines.</summary>
    /// <remarks>
    /// The <c># @missing: XXXX..YYYY; value</c> lines, which give the value of the code points the file does not
    /// list, are returned too, flagged, in the order of the file: a later one overrides an earlier one.
    /// </remarks>
    public static List<UcdLine> ParseLines(string content)
    {
        const string MissingPrefix = "# @missing:";
        var result = new List<UcdLine>();
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            var isMissing = false;
            if (line.StartsWith(MissingPrefix, StringComparison.Ordinal))
            {
                isMissing = true;
                line = line[MissingPrefix.Length..].Trim();
            }

            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                line = line[..commentIndex].Trim();
            }

            if (line.Length == 0)
                continue;

            var fields = line.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
                continue;

            var rangeParts = fields[0].Split("..", StringSplitOptions.TrimEntries);
            if (!int.TryParse(rangeParts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var start))
            {
                if (isMissing)
                    continue;

                throw new InvalidOperationException("Invalid code point in line: " + rawLine);
            }

            var end = rangeParts.Length > 1
                ? int.Parse(rangeParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : start;
            if (start < 0 || end < start || end > 0x10FFFF)
                throw new InvalidOperationException("Invalid range in line: " + rawLine);

            result.Add(new UcdLine(start, end, fields[1..], isMissing));
        }

        return result;
    }
}

internal sealed record UcdLine(int Start, int End, string[] Fields, bool IsMissing);

/// <summary>The values of the enumerated properties, from PropertyValueAliases.txt.</summary>
internal sealed class UcdAliases
{
    private readonly Dictionary<string, List<string[]>> _values = new(StringComparer.Ordinal);

    public static async Task<UcdAliases> LoadAsync()
    {
        var result = new UcdAliases();
        var content = await Ucd.DownloadAsync("ucd/PropertyValueAliases.txt");
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

            var fields = line.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length < 3)
                continue;

            if (!result._values.TryGetValue(fields[0], out var list))
            {
                list = [];
                result._values[fields[0]] = list;
            }

            // ccc has a numeric field before the names; every other property has the short name then the long one.
            list.Add(fields[1..]);
        }

        return result;
    }

    /// <summary>Gets the long names of the values of a property, in the order of the file.</summary>
    public List<string> GetLongNames(string property)
    {
        return _values[property].Select(names => names[1]).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Gets the short name of each value of a property, by long name.</summary>
    public Dictionary<string, string> GetShortNames(string property)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var names in _values[property])
        {
            result.TryAdd(names[1], names[0]);
        }

        return result;
    }

    /// <summary>Gets the long name of a value from any of its names.</summary>
    public string GetLongName(string property, string anyName)
    {
        foreach (var names in _values[property])
        {
            foreach (var name in names)
            {
                if (string.Equals(name, anyName, StringComparison.Ordinal))
                    return names[1];
            }
        }

        throw new InvalidOperationException($"Unknown value '{anyName}' for the property '{property}'");
    }
}

/// <summary>The URLs of the files the generator reads, for the pinned version.</summary>
internal static class UcdUrls
{
    public static string Confusables => Ucd.GetUrl("security/confusables.txt");

    public static string UnicodeData => Ucd.GetUrl("ucd/UnicodeData.txt");

    public static string Blocks => Ucd.GetUrl("ucd/Blocks.txt");

    public static string EmojiData => Ucd.GetUrl("ucd/emoji/emoji-data.txt");

    public static string Scripts => Ucd.GetUrl("ucd/Scripts.txt");

    public static string PropertyValueAliases => Ucd.GetUrl("ucd/PropertyValueAliases.txt");

    public static string ScriptExtensions => Ucd.GetUrl("ucd/ScriptExtensions.txt");
}
