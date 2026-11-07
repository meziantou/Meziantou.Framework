using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Maps the positions of generated scripts (bundles, the output of a compiler) to their original sources with source
/// maps: the location of a <see cref="JsException"/> and the stack traces. It does not change the execution of the
/// scripts: the errors and the <c>stack</c> property that the scripts see keep the generated positions.
/// </summary>
/// <remarks>
/// The source map of a script is found by the URL of the script (the URL given to <see cref="JsRealm.Execute(string, string?)"/>
/// or to <see cref="JsRealm.ParseModule(string, string?, object?)"/>): added with <see cref="Add"/>, or returned by the
/// resolver of the constructor, which is called once per URL. A position whose script has no source map, or that no
/// mapping of the source map contains, is not changed. The methods can be called from any thread.
/// </remarks>
public sealed class JsSourceMapper
{
    private readonly Func<string, JsSourceMap?>? _resolver;
    private readonly ConcurrentDictionary<string, JsSourceMap?> _sourceMaps = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="JsSourceMapper"/> class whose source maps are added with <see cref="Add"/>.</summary>
    public JsSourceMapper()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsSourceMapper"/> class that gets the source maps from a resolver.</summary>
    /// <param name="resolver">
    /// Returns the source map of the script of a URL, or null when it has none (e.g. it reads the file of the
    /// <c>//# sourceMappingURL=</c> comment of the script). It is called once per URL; an exception it throws goes to the
    /// caller of the method that maps the position, and the resolver is called again for the next position of the URL.
    /// </param>
    public JsSourceMapper(Func<string, JsSourceMap?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>Sets the source map of the script of a URL.</summary>
    /// <param name="url">The URL of the generated script.</param>
    /// <param name="sourceMap">The source map of the script.</param>
    public void Add(string url, JsSourceMap sourceMap)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(sourceMap);
        _sourceMaps[url] = sourceMap;
    }

    /// <summary>Gets the original position of a position of a generated script, or the position itself when it cannot be mapped.</summary>
    public JsSourceLocation MapLocation(JsSourceLocation location)
    {
        if (location.Url is { } url && GetSourceMap(url) is { } sourceMap && sourceMap.TryGetOriginalLocation(location.Line, location.Column, out var original))
            return original;

        return location;
    }

    /// <summary>Gets the original position of a position of a generated script (e.g. <see cref="JsException.Location"/>), or the position itself when it cannot be mapped.</summary>
    public JsSourceLocation? MapLocation(JsSourceLocation? location) => location is { } value ? MapLocation(value) : null;

    /// <summary>
    /// Replaces the positions of the frames of a stack trace (the <c>stack</c> property of an error, or the result of
    /// <see cref="JsEngine.CaptureStackTrace"/>) by their original positions. The lines that are not frames, and the
    /// frames that cannot be mapped, are not changed.
    /// </summary>
    /// <param name="stackTrace">The stack trace: lines such as <c>    at check (bundle.js:2:24)</c> or <c>    at bundle.js:5:1</c>.</param>
    public string MapStackTrace(string stackTrace)
    {
        ArgumentNullException.ThrowIfNull(stackTrace);
        var builder = new StringBuilder(stackTrace.Length);
        var lineStart = 0;
        while (lineStart <= stackTrace.Length)
        {
            var lineEnd = stackTrace.IndexOf("\n", lineStart, StringComparison.Ordinal);
            if (lineEnd < 0)
            {
                lineEnd = stackTrace.Length;
            }

            AppendFrame(builder, stackTrace.AsSpan(lineStart, lineEnd - lineStart));
            if (lineEnd == stackTrace.Length)
                break;

            builder.Append('\n');
            lineStart = lineEnd + 1;
        }

        return builder.ToString();
    }

    private void AppendFrame(StringBuilder builder, ReadOnlySpan<char> line)
    {
        // "    at name (url:line:column)" or "    at url:line:column"
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
        {
            builder.Append(line);
            return;
        }

        int start, end;
        if (line.EndsWith(")") && line.LastIndexOf('(') is var open and >= 0)
        {
            start = open + 1;
            end = line.Length - 1;
        }
        else
        {
            start = line.Length - trimmed.Length + 3;
            end = line.Length;
        }

        var text = line[start..end];
        var columnSeparator = text.LastIndexOf(':');
        var lineSeparator = columnSeparator > 0 ? text[..columnSeparator].LastIndexOf(':') : -1;
        if (lineSeparator > 0
            && int.TryParse(text[(lineSeparator + 1)..columnSeparator], NumberStyles.None, CultureInfo.InvariantCulture, out var lineNumber)
            && int.TryParse(text[(columnSeparator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var columnNumber))
        {
            var location = new JsSourceLocation(text[..lineSeparator].ToString(), lineNumber, columnNumber);
            var mapped = MapLocation(location);
            if (mapped != location)
            {
                builder.Append(line[..start])
                    .Append(mapped.Url)
                    .Append(':').Append(mapped.Line.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(mapped.Column.ToString(CultureInfo.InvariantCulture))
                    .Append(line[end..]);
                return;
            }
        }

        builder.Append(line);
    }

    private JsSourceMap? GetSourceMap(string url)
    {
        if (_sourceMaps.TryGetValue(url, out var sourceMap))
            return sourceMap;

        if (_resolver is null)
            return null;

        return _sourceMaps.GetOrAdd(url, _resolver);
    }
}
