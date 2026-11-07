using System.Globalization;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.JavaScriptEngine.Fuzz;

/// <summary>
/// <see cref="JsSourceMap.Parse"/> rejects an invalid source map with a <see cref="FormatException"/> only, and
/// <see cref="JsSourceMap.TryParse"/> agrees with it. A parsed source map maps a 1-based position to a 1-based position of
/// one of its sources, or does not map it; a position that is not 1-based is never mapped. A source map built from
/// known mappings maps each position to the original position of the last mapping of its line that starts at or before
/// it, like a direct search of the mappings. <see cref="JsSourceMapper.MapStackTrace"/> only replaces the positions of the
/// frames that the source map maps, and keeps the other lines unchanged.
/// </summary>
public static partial class JavaScriptEngineFuzzTargets
{
    private const string Base64Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private const string GeneratedScriptUrl = "bundle.js";

    // The URLs of the source map, against which the relative sources are resolved
    private static readonly string?[] SourceMapUrls =
    [
        null, "bundle.js.map", "dist/js/bundle.js.map", "/abs/bundle.js.map", "../up/bundle.js.map", "https://example.com/js/bundle.js.map", "https://example.com", "file:///C:/src/bundle.js.map",
        "C:\\src\\bundle.js.map", "C:/src/bundle.js.map", "webpack://app/bundle.js.map", "a:b", "http://[::1]:80/x.map", "data:application/json,{}", "", "//host/path.map", "x/./y/../z.map",
    ];

    // A property: the field initializers of the partial files run in no particular order, and All reads it
    private static string[] SourceMapTokens =>
    [
        "{", "}", "[", "]", ":", ",", "\"", "\\", "\\u0000", "null", "true", "3", "3.0", "-1", "2147483647", "4294967296",
        "\"version\"", "\"file\"", "\"sourceRoot\"", "\"sources\"", "\"sourcesContent\"", "\"names\"", "\"mappings\"", "\"sections\"", "\"offset\"", "\"line\"", "\"column\"", "\"map\"", "\"x_google_ignoreList\"", "\"ignoreList\"",
        "\"a.js\"", "\"../src/a.ts\"", "\"/abs.js\"", "\"webpack:///./src/b.ts\"", "\"https://example.com/c.js\"", "\"./\"", "\"..\"", "\"src/\"", "\"C:\\\\x.js\"",
        "AAAA", "AACA", "AAAC", "ACAA", "CAAA", "A", "B", "D", "g", "f", "/", "+", "gB", "//////D", "+/+/+/+/+/+/+/+/", ";", ";;", ",", ",,", "AAAAA", "AAAAAA",
        "\n", "    at ", "at ", " (", ")", "bundle.js:1:1", "bundle.js:", ":1:1", ":0:0", ":-1:5", ":99999999999:1", "eval at ", "<anonymous>", "Error: x\n",
    ];

    // The input is the URL of the source map (an index into SourceMapUrls), its JSON text, NUL, and a stack trace
    private static void SourceMap(ReadOnlySpan<byte> data)
    {
        var url = FuzzInput.Pick(SourceMapUrls, FuzzInput.ReadByte(ref data));
        var json = FuzzText.Decode(FuzzInput.ReadUntil(ref data, Separator));
        var stackTrace = FuzzText.Decode(data);

        JsSourceMap? sourceMap;
        try
        {
            sourceMap = JsSourceMap.Parse(json, url);
        }
        catch (FormatException)
        {
            sourceMap = null;
        }

        FuzzAssert.Equal(sourceMap is not null, JsSourceMap.TryParse(json, url, out var other), "TryParse and Parse do not agree on whether the source map is valid");

        // A mapper without source map changes nothing
        FuzzAssert.Equal(stackTrace, new JsSourceMapper().MapStackTrace(stackTrace), "A mapper without source maps changes the stack trace");
        if (sourceMap is null)
            return;

        FuzzAssert.True(other is not null, "TryParse returned true without a source map");
        FuzzAssert.True(sourceMap.Sources.Count == other.Sources.Count && sourceMap.Sources.SequenceEqual(other.Sources, StringComparer.Ordinal), "The source map is parsed differently twice");
        CheckSourceCount(json, sourceMap);

        // Every position, the ones that are not 1-based included, is mapped to a 1-based position of a source, or not mapped
        var sources = sourceMap.Sources.ToHashSet(StringComparer.Ordinal);
        int[] lines = [int.MinValue, -1, 0, 1, 2, 3, 4, 5, 8, 16, 100, int.MaxValue];
        int[] columns = [int.MinValue, -1, 0, 1, 2, 3, 5, 8, 16, 32, 100, 1000, int.MaxValue];
        foreach (var line in lines)
        {
            foreach (var column in columns)
            {
                var isMapped = sourceMap.TryGetOriginalLocation(line, column, out var location);
                FuzzAssert.Equal(isMapped, other.TryGetOriginalLocation(line, column, out var otherLocation), $"The position {line}:{column} is mapped differently by two parses of the source map");
                FuzzAssert.Equal(location, otherLocation, $"The position {line}:{column} is mapped differently by two parses of the source map");
                if (!isMapped)
                    continue;

                FuzzAssert.True(line > 0 && column > 0, $"The position {line}:{column}, which is not 1-based, is mapped to {location}");
                FuzzAssert.True(location.Line > 0 && location.Column > 0, $"The position {line}:{column} is mapped to {location}, which is not 1-based");
                FuzzAssert.True(location.Url is not null && sources.Contains(location.Url), $"The position {line}:{column} is mapped to {location}, which is not in a source of the source map");
            }
        }

        // The mapper of a resolver gives the same result as the mapper of Add; the lines that are not frames are unchanged
        var mapper = new JsSourceMapper();
        mapper.Add(GeneratedScriptUrl, sourceMap);
        var mapped = mapper.MapStackTrace(stackTrace);
        FuzzAssert.Equal(mapped, new JsSourceMapper(u => u == GeneratedScriptUrl ? sourceMap : null).MapStackTrace(stackTrace), "The mapper of a resolver and the mapper of Add map the stack trace differently");
        if (sourceMap.Sources.Any(source => source.Contains('\n', StringComparison.Ordinal)))
            return;

        var originalLines = stackTrace.Split('\n');
        var mappedLines = mapped.Split('\n');
        FuzzAssert.Equal(originalLines.Length, mappedLines.Length, "MapStackTrace changes the number of lines of the stack trace");
        for (var i = 0; i < originalLines.Length; i++)
        {
            if (!originalLines[i].AsSpan().TrimStart().StartsWith("at ", StringComparison.Ordinal))
            {
                FuzzAssert.Equal(originalLines[i], mappedLines[i], $"MapStackTrace changes the line {i + 1}, which is not a frame");
            }
        }
    }

    // A source map without sections has one source per item of its "sources" array
    private static void CheckSourceCount(string json, JsSourceMap sourceMap)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("sections", out _))
            return;

        FuzzAssert.Equal(document.RootElement.GetProperty("sources").GetArrayLength(), sourceMap.Sources.Count, "The source map does not have one source per item of its sources");
    }

    private static IEnumerable<FuzzSeed> GetSourceMapSeeds()
    {
        const string Stack = "Error: boom\n    at check (bundle.js:1:5)\n    at bundle.js:2:1\n    at other.js:1:1\n    at <anonymous>\nnot a frame bundle.js:1:1";
        string[] maps =
        [
            """{"version":3,"file":"bundle.js","sources":["a.js","b.js"],"names":["x"],"mappings":"AAAA,IAAI;ACAA,MAAMA;;A;CAAC"}""",
            """{"version":3,"sourceRoot":"src/","sources":["a.ts","../b.ts","webpack:///c.ts",null],"mappings":"AAAA;AACA,EAAE"}""",
            """{"version":3,"sources":["./x/../y.js","/abs.js"],"sourcesContent":["a"],"mappings":"AAAA,CAAC,CAAC;AACD"}""",
            """{"version":3,"sections":[{"offset":{"line":0,"column":0},"map":{"version":3,"sources":["a.js"],"mappings":"AAAA"}},{"offset":{"line":1,"column":4},"map":{"version":3,"sources":["b.js"],"mappings":"AAAA,EAAE"}}]}""",
            """{"version":3,"sources":["a.js"],"mappings":"gBAAgB,+/////DAA+/////D"}""",
            """{"version":3,"sources":[],"mappings":""}""",
            """{"version":3,"sources":["a.js"],"mappings":"AA+/////D+/////D"}""",
            """{"version":2,"sources":["a.js"],"mappings":"AAAA"}""",
            """{"version":3,"sources":["a.js"],"mappings":"AAAAAA"}""",
            """{"version":3,"sources":["a.js"],"mappings":"AAAA;!"}""",
        ];

        var index = 0;
        foreach (var map in maps)
        {
            yield return new FuzzSeed($"source-map-{index}", (byte[])[(byte)(index % SourceMapUrls.Length), .. FuzzText.Encode(map), Separator, .. FuzzText.Encode(Stack)]);
            index++;
        }
    }

    // The input builds the mappings of a source map: the sources and the sections, then a list of segments and line breaks.
    // The target writes the source map, parses it, and maps the positions around each segment
    private static void SourceMapMappings(ReadOnlySpan<byte> data)
    {
        var model = MappingModel.Read(ref data);
        var json = model.ToJson();
        if (model.HasUnrepresentablePosition)
        {
            FuzzAssert.False(JsSourceMap.TryParse(json, url: null, out _), "The source map is valid, but one of its original positions has no 1-based position: " + json);
            return;
        }

        if (!JsSourceMap.TryParse(json, url: null, out var sourceMap))
        {
            FuzzAssert.Fail("The source map built from valid mappings is rejected: " + json);
        }

        FuzzAssert.True(model.Sources.SequenceEqual(sourceMap.Sources, StringComparer.Ordinal), "The sources of the source map are not its sources: " + string.Join(", ", sourceMap.Sources));

        var mapper = new JsSourceMapper();
        mapper.Add(GeneratedScriptUrl, sourceMap);
        var checkedPositions = 0;
        foreach (var (line, column) in model.GetPositions())
        {
            if (checkedPositions++ > 2000)
                break;

            var isExpected = model.TryGetOriginalLocation(line, column, out var expected);
            var isMapped = sourceMap.TryGetOriginalLocation(line, column, out var actual);
            FuzzAssert.Equal(isExpected, isMapped, $"The position {line}:{column} is {(isMapped ? "mapped to " + actual : "not mapped")} instead of {(isExpected ? expected : "not mapped")}: {json}");
            if (isExpected)
            {
                FuzzAssert.True(actual.Line > 0 && actual.Column > 0, $"The position {line}:{column} is mapped to {actual}, which is not 1-based: {json}");
                FuzzAssert.Equal(expected, actual, $"The position {line}:{column} is mapped to {actual} instead of {expected}: {json}");
            }

            // The frames of a stack trace are mapped the same way
            var frame = string.Create(CultureInfo.InvariantCulture, $"    at f ({GeneratedScriptUrl}:{line}:{column})");
            var expectedFrame = isExpected ? string.Create(CultureInfo.InvariantCulture, $"    at f ({expected.Url}:{expected.Line}:{expected.Column})") : frame;
            FuzzAssert.Equal(expectedFrame, mapper.MapStackTrace(frame), $"The frame of the position {line}:{column} is not mapped like the position: {json}");
        }
    }

    private static IEnumerable<FuzzSeed> GetSourceMapMappingsSeeds()
    {
        // header (sections, sources), offsets, then operations: 0 new line, 1 generated column only, other a mapping
        yield return new FuzzSeed("mappings-simple", (byte[])[0, 2, 0, 0, 0, 2, 4, 0, 0, 4, 0, 0, 0, 3, 0, 1, 2, 5, 1, 7, 3]);
        yield return new FuzzSeed("mappings-unsorted", (byte[])[3, 10, 2, 0, 3, 1, 0, 2, 2, 5, 1, 1, 1, 4, 1, 7, 0, 0, 5, 0, 1, 1]);
        yield return new FuzzSeed("mappings-sections", (byte[])[1, 3, 7, 2, 0, 0, 0, 2, 4, 1, 1, 1, 0, 6, 2, 0, 0, 9]);
        yield return new FuzzSeed("mappings-two-sections", (byte[])[5, 2, 5, 2, 3, 1, 1, 0, 2, 3, 0, 2, 0, 1, 0, 0, 2, 0, 0, 1, 1]);
        yield return new FuzzSeed("mappings-large", (byte[])[0, 2, 255, 0, 251, 252, 253, 2, 254, 0, 255, 255, 0, 2, 0, 0, 250, 251]);
        yield return new FuzzSeed("mappings-max-values", (byte[])[0, 2, 0, 0, 250, 250, 0, 1, 250, 2, 251, 0, 251, 251]);
        yield return new FuzzSeed("mappings-duplicate-columns", (byte[])[0, 2, 4, 0, 1, 2, 3, 2, 4, 0, 5, 6, 1, 4, 2, 4, 0, 7, 8]);
    }

    /// <summary>Mappings, their source map, and a direct search of the original position of a position.</summary>
    private sealed class MappingModel
    {
        private static readonly string[] SourceNames = ["a.js", "src/b.ts", "webpack:///c.ts", "d e (1).js"];

        private readonly List<Segment> _segments = [];
        private readonly List<(int Line, int Column)> _sections = [];
        private int _lineCount = 1;

        public string[] Sources { get; private set; } = [];

        // An original line or column of int.MaxValue: its 1-based position is not an int
        public bool HasUnrepresentablePosition => _segments.Exists(segment => segment.Source >= 0 && (segment.OriginalLine is int.MaxValue || segment.OriginalColumn is int.MaxValue));

        // header: sections mode (none, one at an offset, two) and source count; the offset of the section; then the operations
        public static MappingModel Read(ref ReadOnlySpan<byte> data)
        {
            var model = new MappingModel();
            var header = FuzzInput.ReadByte(ref data);
            model.Sources = SourceNames[..(1 + (header / 3 % SourceNames.Length))];
            switch (header % 3)
            {
                case 1:
                    model._sections.Add((ReadValue(ref data), ReadValue(ref data)));
                    break;
                case 2:
                    model._sections.Add((0, 0));
                    model._sections.Add((ReadValue(ref data), ReadValue(ref data)));
                    break;
            }

            var line = 0;
            while (!data.IsEmpty && model._segments.Count < 500)
            {
                var operation = FuzzInput.ReadByte(ref data);
                switch (operation % 8)
                {
                    case 0:
                        line++;
                        model._lineCount++;
                        break;
                    case 1:
                        model._segments.Add(new Segment(line, ReadValue(ref data), Source: -1, 0, 0, HasName: false));
                        break;
                    default:
                        var column = ReadValue(ref data);
                        var source = FuzzInput.ReadByte(ref data) % model.Sources.Length;
                        model._segments.Add(new Segment(line, column, source, ReadValue(ref data), ReadValue(ref data), HasName: (operation & 8) != 0));
                        break;
                }
            }

            return model;
        }

        // A small value, a value of a few thousands, or a value at the limits of the 32-bit values of the mappings
        private static int ReadValue(ref ReadOnlySpan<byte> data)
        {
            var value = FuzzInput.ReadByte(ref data);
            return value switch
            {
                < 200 => value,
                < 250 => ((value - 200) * 1000) + FuzzInput.ReadByte(ref data),
                250 => int.MaxValue,
                251 => int.MaxValue - 1,
                252 => 1 << 30,
                253 => (1 << 30) - 1,
                254 => 1 << 16,
                _ => 65535,
            };
        }

        public string ToJson()
        {
            var map = ToJsonMap();
            if (_sections.Count is 0)
                return map;

            var sections = string.Join(',', _sections.Select(section => string.Create(CultureInfo.InvariantCulture, $$"""{"offset":{"line":{{section.Line}},"column":{{section.Column}}},"map":{{map}}}""")));
            return $$"""{"version":3,"sections":[{{sections}}]}""";
        }

        private string ToJsonMap()
        {
            var mappings = new StringBuilder();
            int source = 0, originalLine = 0, originalColumn = 0;
            for (var line = 0; line < _lineCount; line++)
            {
                if (line > 0)
                {
                    mappings.Append(';');
                }

                var generatedColumn = 0;
                var first = true;
                foreach (var segment in _segments.Where(segment => segment.Line == line))
                {
                    if (!first)
                    {
                        mappings.Append(',');
                    }

                    first = false;
                    AppendVlq(mappings, segment.Column - generatedColumn);
                    generatedColumn = segment.Column;
                    if (segment.Source < 0)
                        continue;

                    AppendVlq(mappings, segment.Source - source);
                    AppendVlq(mappings, segment.OriginalLine - originalLine);
                    AppendVlq(mappings, segment.OriginalColumn - originalColumn);
                    (source, originalLine, originalColumn) = (segment.Source, segment.OriginalLine, segment.OriginalColumn);
                    if (segment.HasName)
                    {
                        // The only name: its index does not change
                        AppendVlq(mappings, 0);
                    }
                }
            }

            var sources = string.Join(',', Sources.Select(source => JsonSerializer.Serialize(source)));
            return $$"""{"version":3,"sources":[{{sources}}],"names":["n"],"mappings":"{{mappings}}"}""";
        }

        private static void AppendVlq(StringBuilder builder, int value)
        {
            var remaining = value < 0 ? (-(long)value << 1) | 1 : (long)value << 1;
            do
            {
                var digit = (int)(remaining & 31);
                remaining >>= 5;
                if (remaining > 0)
                {
                    digit |= 32;
                }

                builder.Append(Base64Characters[digit]);
            }
            while (remaining > 0);
        }

        // The 1-based positions of the segments, around them, and outside the mappings
        public IEnumerable<(int Line, int Column)> GetPositions()
        {
            IEnumerable<(int Line, int Column)> starts = _sections.Count is 0 ? [(0, 0)] : _sections;
            foreach (var (sectionLine, sectionColumn) in starts)
            {
                foreach (var segment in _segments)
                {
                    var line = (long)sectionLine + segment.Line + 1;
                    var column = (segment.Line is 0 ? (long)sectionColumn : 0) + segment.Column + 1;
                    foreach (var (l, c) in new[] { (line, column), (line, column - 1), (line, column + 1), (line - 1, column), (line + 1, column) })
                    {
                        if (l is >= 1 and <= int.MaxValue && c is >= 1 and <= int.MaxValue)
                            yield return ((int)l, (int)c);
                    }
                }

                yield return (sectionLine + 1 > 0 ? sectionLine + 1 : 1, 1);
            }

            yield return (_lineCount + 1, 1);
            yield return (1, int.MaxValue);
            yield return (int.MaxValue, int.MaxValue);
        }

        // The last section that starts at or before the position, then the last segment of its line that starts at or
        // before the column (the last of the segments of the same column)
        public bool TryGetOriginalLocation(int line, int column, out JsSourceLocation location)
        {
            location = default;
            long generatedLine = line - 1L;
            long generatedColumn = column - 1L;
            if (_sections.Count > 0)
            {
                var index = _sections.FindLastIndex(section => section.Line < generatedLine || (section.Line == generatedLine && section.Column <= generatedColumn));
                if (index < 0)
                    return false;

                var (sectionLine, sectionColumn) = _sections[index];
                if (generatedLine == sectionLine)
                {
                    generatedColumn -= sectionColumn;
                }

                generatedLine -= sectionLine;
            }

            Segment? found = null;
            foreach (var segment in _segments)
            {
                if (segment.Line == generatedLine && segment.Column <= generatedColumn && (found is null || segment.Column >= found.Value.Column))
                {
                    found = segment;
                }
            }

            if (found is not { Source: >= 0 } result)
                return false;

            location = new JsSourceLocation(Sources[result.Source], result.OriginalLine + 1, result.OriginalColumn + 1);
            return true;
        }

        private readonly record struct Segment(int Line, int Column, int Source, int OriginalLine, int OriginalColumn, bool HasName);
    }
}
