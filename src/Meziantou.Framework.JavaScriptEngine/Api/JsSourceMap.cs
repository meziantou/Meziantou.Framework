using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A source map (https://tc39.es/ecma426/, version 3): the positions of the original sources of a generated script, such
/// as a bundle or the output of a compiler. Use it with a <see cref="JsSourceMapper"/> to show the errors of the script at
/// their original positions.
/// </summary>
public sealed class JsSourceMap
{
    private const string Base64Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private readonly Segment[][]? _lines;
    private readonly Section[]? _sections;
    private readonly string[] _sources;

    private JsSourceMap(string? file, string[] sources, Segment[][]? lines, Section[]? sections)
    {
        File = file;
        _sources = sources;
        _lines = lines;
        _sections = sections;
    }

    /// <summary>Gets the name of the generated file (the <c>file</c> field), or null.</summary>
    public string? File { get; }

    /// <summary>
    /// Gets the URLs of the original sources: the <c>sources</c> field, prefixed by <c>sourceRoot</c> and resolved against
    /// the URL of the source map when they are relative.
    /// </summary>
    public IReadOnlyList<string> Sources => _sources;

    /// <summary>Parses a source map.</summary>
    /// <param name="json">The JSON text of the source map.</param>
    /// <param name="url">The URL (or the path) of the source map, against which the relative URLs of the sources are resolved, or null.</param>
    /// <exception cref="FormatException">The text is not a valid source map of version 3.</exception>
    public static JsSourceMap Parse(string json, string? url = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            return Parse(document.RootElement, url);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The source map is not valid JSON: " + ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new FormatException("The source map is not valid: " + ex.Message, ex);
        }
    }

    /// <summary>Parses a source map; returns false when the text is not a valid source map of version 3.</summary>
    /// <param name="json">The JSON text of the source map.</param>
    /// <param name="url">The URL (or the path) of the source map, against which the relative URLs of the sources are resolved, or null.</param>
    /// <param name="sourceMap">The source map.</param>
    public static bool TryParse(string json, string? url, [NotNullWhen(true)] out JsSourceMap? sourceMap)
    {
        try
        {
            sourceMap = Parse(json, url);
            return true;
        }
        catch (FormatException)
        {
            sourceMap = null;
            return false;
        }
    }

    /// <summary>Gets the original position of a position of the generated code.</summary>
    /// <param name="line">The 1-based line in the generated code.</param>
    /// <param name="column">The 1-based column (in UTF-16 code units) in the generated code.</param>
    /// <param name="location">The original position: the URL of the original source, and the 1-based line and column of the mapping that contains the position.</param>
    /// <returns>false when no mapping contains the position, or when its mapping has no original source.</returns>
    public bool TryGetOriginalLocation(int line, int column, out JsSourceLocation location) => TryGetOriginalLocationCore(line - 1, column - 1, out location);

    private bool TryGetOriginalLocationCore(int line, int column, out JsSourceLocation location)
    {
        location = default;
        if (line < 0 || column < 0)
            return false;

        if (_sections is not null)
        {
            // The last section that starts before the position
            for (var i = _sections.Length - 1; i >= 0; i--)
            {
                var section = _sections[i];
                if (section.Line < line || (section.Line == line && section.Column <= column))
                    return section.Map.TryGetOriginalLocationCore(line - section.Line, line == section.Line ? column - section.Column : column, out location);
            }

            return false;
        }

        if (line >= _lines!.Length)
            return false;

        // The last segment of the line that starts at or before the column
        var segments = _lines[line];
        var low = 0;
        var high = segments.Length - 1;
        var found = -1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (segments[middle].GeneratedColumn <= column)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found < 0 || segments[found].Source < 0)
            return false;

        var segment = segments[found];
        location = new JsSourceLocation(_sources[segment.Source], segment.Line + 1, segment.Column + 1);
        return true;
    }

    private static JsSourceMap Parse(JsonElement root, string? url)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("The source map is not an object");

        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetDouble() != 3)
            throw new FormatException("The version of the source map is not 3");

        var file = root.TryGetProperty("file", out var fileElement) && fileElement.ValueKind == JsonValueKind.String ? fileElement.GetString() : null;
        if (root.TryGetProperty("sections", out var sectionsElement))
        {
            if (sectionsElement.ValueKind != JsonValueKind.Array)
                throw new FormatException("The sections of the source map are not an array");

            var sections = new List<Section>();
            var sources = new List<string>();
            foreach (var sectionElement in sectionsElement.EnumerateArray())
            {
                if (!sectionElement.TryGetProperty("offset", out var offset) || !sectionElement.TryGetProperty("map", out var mapElement))
                    throw new FormatException("A section of the source map has no offset or no map");

                var map = Parse(mapElement, url);
                var section = new Section(GetInt32(offset, "line"), GetInt32(offset, "column"), map);
                if (sections.Count > 0 && (section.Line < sections[^1].Line || (section.Line == sections[^1].Line && section.Column < sections[^1].Column)))
                    throw new FormatException("The sections of the source map are not sorted");

                sections.Add(section);
                sources.AddRange(map._sources);
            }

            return new JsSourceMap(file, [.. sources.Distinct(StringComparer.Ordinal)], lines: null, [.. sections]);
        }

        var sourceRoot = root.TryGetProperty("sourceRoot", out var sourceRootElement) && sourceRootElement.ValueKind == JsonValueKind.String ? sourceRootElement.GetString() : null;
        if (!root.TryGetProperty("sources", out var sourcesElement) || sourcesElement.ValueKind != JsonValueKind.Array)
            throw new FormatException("The source map has no sources");

        var resolvedSources = new string[sourcesElement.GetArrayLength()];
        var index = 0;
        foreach (var source in sourcesElement.EnumerateArray())
        {
            resolvedSources[index++] = ResolveSourceUrl(url, sourceRoot, source.ValueKind == JsonValueKind.String ? source.GetString()! : "");
        }

        if (!root.TryGetProperty("mappings", out var mappings) || mappings.ValueKind != JsonValueKind.String)
            throw new FormatException("The source map has no mappings");

        return new JsSourceMap(file, resolvedSources, DecodeMappings(mappings.GetString()!, resolvedSources.Length), sections: null);
    }

    private static int GetInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result < 0)
            throw new FormatException("The " + name + " of the offset of a section is not valid");

        return result;
    }

    private static Segment[][] DecodeMappings(string mappings, int sourceCount)
    {
        var lines = new List<Segment[]>();
        var segments = new List<Segment>();
        var fields = new int[5];
        int source = 0, originalLine = 0, originalColumn = 0;
        var position = 0;
        while (true)
        {
            var generatedColumn = 0;
            segments.Clear();
            while (position < mappings.Length && mappings[position] != ';')
            {
                if (mappings[position] == ',')
                {
                    position++;
                    continue;
                }

                var count = 0;
                while (position < mappings.Length && mappings[position] is not (',' or ';'))
                {
                    if (count == fields.Length)
                        throw new FormatException("A segment of the mappings has more than 5 fields");

                    fields[count++] = DecodeVlq(mappings, ref position);
                }

                if (count is not (1 or 4 or 5))
                    throw new FormatException("A segment of the mappings has " + count + " fields");

                generatedColumn += fields[0];
                if (generatedColumn < 0)
                    throw new FormatException("A segment of the mappings has a negative column");

                if (count == 1)
                {
                    segments.Add(new Segment(generatedColumn, -1, 0, 0));
                    continue;
                }

                source += fields[1];
                originalLine += fields[2];
                originalColumn += fields[3];
                if (source < 0 || source >= sourceCount || originalLine < 0 || originalColumn < 0)
                    throw new FormatException("A segment of the mappings refers to an invalid source position");

                segments.Add(new Segment(generatedColumn, source, originalLine, originalColumn));
            }

            // The segments are sorted by column (stable: the first mapping of a column is kept first)
            lines.Add([.. segments.OrderBy(static segment => segment.GeneratedColumn)]);
            if (position >= mappings.Length)
                break;

            position++;
        }

        return [.. lines];
    }

    private static int DecodeVlq(string text, ref int position)
    {
        var result = 0L;
        var shift = 0;
        while (true)
        {
            if (position >= text.Length)
                throw new FormatException("A value of the mappings is truncated");

            var digit = Base64Characters.IndexOf(text[position++], StringComparison.Ordinal);
            if (digit < 0)
                throw new FormatException("The mappings contain an invalid character");

            if (shift > 30)
                throw new FormatException("A value of the mappings is too large");

            result |= (long)(digit & 31) << shift;
            shift += 5;
            if ((digit & 32) == 0)
                break;
        }

        var value = result >> 1;
        if (value > int.MaxValue)
            throw new FormatException("A value of the mappings is too large");

        return (result & 1) != 0 ? -(int)value : (int)value;
    }

    /// <summary>The URL of a source: sourceRoot + source, resolved against the URL of the source map.</summary>
    private static string ResolveSourceUrl(string? mapUrl, string? sourceRoot, string source)
    {
        if (!HasScheme(source) && !string.IsNullOrEmpty(sourceRoot))
        {
            source = sourceRoot.EndsWith("/", StringComparison.Ordinal) ? sourceRoot + source : sourceRoot + "/" + source;
        }

        if (HasScheme(source) || string.IsNullOrEmpty(mapUrl))
            return source;

        if (HasScheme(mapUrl) && Uri.TryCreate(mapUrl, UriKind.Absolute, out var baseUri) && Uri.TryCreate(baseUri, source, out var resolved))
            return resolved.ToString();

        if (source.StartsWith("/", StringComparison.Ordinal))
            return NormalizePath(source);

        var directoryEnd = mapUrl.LastIndexOf("/", StringComparison.Ordinal);
        return NormalizePath(directoryEnd < 0 ? source : string.Concat(mapUrl.AsSpan(0, directoryEnd + 1), source));
    }

    private static bool HasScheme(string url)
    {
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon < 2 || !char.IsAsciiLetter(url[0]))
            return false;

        for (var i = 1; i < colon; i++)
        {
            if (!char.IsAsciiLetterOrDigit(url[i]) && url[i] is not ('+' or '-' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>Removes the <c>.</c> and <c>..</c> segments of a relative path.</summary>
    private static string NormalizePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/'))
        {
            if (segment == ".")
                continue;

            if (segment == ".." && segments.Count > 0 && segments[^1] is not (".." or ""))
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(int GeneratedColumn, int Source, int Line, int Column);

    private sealed record Section(int Line, int Column, JsSourceMap Map);
}
