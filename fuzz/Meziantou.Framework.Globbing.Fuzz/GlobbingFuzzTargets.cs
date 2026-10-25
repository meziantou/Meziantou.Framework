using System.Text;
using System.Text.Unicode;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Globbing.Fuzz;

/// <summary>
/// An invalid pattern must only throw <see cref="ArgumentException"/>, and matching never throws. <see cref="Glob.ToString"/>
/// parses back into a glob that matches the same paths, ignoring the case or allowing a leading dot only adds matches, and a
/// directory holding a match is a partial match. A collection built from globs matches a path when one of its include globs
/// matches it and none of its exclude globs does, whatever their order, and recurses into a directory when one of its include
/// globs does. gitignore content never fails to load, an excluded directory excludes its content, the line endings or a byte
/// order mark do not change what the content matches, and loading it from a reader or from a stream (UTF-8, or any encoding
/// with a byte order mark) gives the same globs as parsing the text.
/// </summary>
/// <remarks>
/// <c>\</c> is a path separator on Windows only, and the seeds are replayed on every platform: the properties that depend on
/// how a path splits into segments skip the paths holding a <c>\</c>.
/// </remarks>
public static class GlobbingFuzzTargets
{
    // Matching backtracks, and its cost grows exponentially with the number of '*' in a segment (documented on Glob), so
    // the targets only match patterns with a few wildcards against short paths
    private const int MaxWildcards = 6;
    private const int MaxPathLength = 48;
    private const int MaxPaths = 8;

    private const byte GitIgnoreSeparator = 0;
    private const byte CollectionSeparator = 0;
    private const int MaxCollectionGlobs = 6;

    private static readonly GlobDialect[] Dialects = Enum.GetValues<GlobDialect>();

    private static readonly string[] Tokens =
    [
        "*", "**", "**/", "/**", "/**/", "?", "[", "]", "[!", "[^", "[a-z]", "[!a-z]", "[]]", "[!]]", "[a-]", "[[:alpha:]]", "[[:upper:]]", "[[:digit:]]", "[[:space:]]", "[[:foo:]]", "[[=a=]]", "[[.-.]]",
        "{", "}", "{a,b}", ",", "!", "\\", "\\*", "/", "//", ".", "..", "./", "../", ".hidden", "%2A", "%2F", "%25", "*.*", "*.cs", "#", " ", "\t", "\r", "\n", "\uFEFF", "a", "A", "\u00E9", "\u00C9", "\u03A3", "\u03C3", "\u03C2", "\u017F", "\u212A",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("glob-parse-match", ParseAndMatch) { Seeds = GetParseAndMatchSeeds, Dictionary = Tokens },
        new("glob-gitignore", GitIgnore) { Seeds = GetGitIgnoreSeeds, Dictionary = Tokens },
        new("glob-collection", Collection) { Seeds = GetCollectionSeeds, Dictionary = Tokens },
    ];

    private static void ParseAndMatch(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var dialect = Dialects[flags % Dialects.Length];
        var options = (GlobOptions)((flags / Dialects.Length) % 4);
        var pattern = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
        var paths = ReadPaths(data);

        var glob = Parse(pattern, dialect, options);
        if (glob is null)
            return;

        var text = glob.ToString();
        FuzzAssert.True(Glob.TryParse(text, dialect, options, out var roundTripped), $"ToString gives a pattern that does not parse: {FuzzAssert.Format(text)}");
        FuzzAssert.Equal(text, roundTripped.ToString(), "ToString of the parsed ToString differs");

        if (CountWildcards(pattern) > MaxWildcards || CountWildcards(text) > MaxWildcards)
            return;

        var pathSeparatorAware = dialect is not GlobDialect.Posix;
        Glob? caseSensitive = null;
        if (options.HasFlag(GlobOptions.IgnoreCase) && !pattern.Contains('[', StringComparison.Ordinal))
        {
            _ = Glob.TryParse(pattern, dialect, options & ~GlobOptions.IgnoreCase, out caseSensitive);
        }

        Glob? withoutLeadingDot = null;
        if (dialect is GlobDialect.Standard && options.HasFlag(GlobOptions.MatchLeadingDot))
        {
            _ = Glob.TryParse(pattern, dialect, options & ~GlobOptions.MatchLeadingDot, out withoutLeadingDot);
        }

        foreach (var path in paths)
        {
            var isMatch = glob.IsMatch(path);
            FuzzAssert.Equal(isMatch, roundTripped.IsMatch(path), $"The glob and its ToString {FuzzAssert.Format(text)} disagree on {FuzzAssert.Format(path)}");
            _ = glob.IsPartialMatch(path);

            // The bracket expressions are excluded: '[!a-z]' matches 'A' only when the case is significant
            if (caseSensitive is not null && caseSensitive.IsMatch(path))
            {
                FuzzAssert.True(isMatch, $"{FuzzAssert.Format(path)} matches the case-sensitive glob, but not the one that ignores the case");
            }

            if (withoutLeadingDot is not null && withoutLeadingDot.IsMatch(path))
            {
                FuzzAssert.True(isMatch, $"{FuzzAssert.Format(path)} matches without {nameof(GlobOptions.MatchLeadingDot)}, but not with it");
            }

            if (!pathSeparatorAware || !IsSimpleRelativePath(path))
                continue;

            FuzzAssert.Equal(isMatch, glob.IsMatch(path.AsSpan(), [], PathItemType.File), $"The item type File changes the match of {FuzzAssert.Format(path)}");
            FuzzAssert.Equal(glob.IsMatch(path + "/"), glob.IsMatch(path.AsSpan(), [], PathItemType.Directory), $"A trailing '/' and the item type Directory disagree on {FuzzAssert.Format(path)}");

            var index = path.LastIndexOf('/', StringComparison.Ordinal);
            if (index > 0)
            {
                FuzzAssert.Equal(isMatch, glob.IsMatch(path[..index], path[(index + 1)..]), $"Splitting {FuzzAssert.Format(path)} into a directory and a file name changes the match");
            }

            // The enumeration only recurses into the directories that are partial matches
            if (isMatch)
            {
                for (var separator = path.IndexOf('/', StringComparison.Ordinal); separator > 0; separator = path.IndexOf('/', separator + 1, StringComparison.Ordinal))
                {
                    FuzzAssert.True(glob.IsPartialMatch(path[..separator]), $"{FuzzAssert.Format(path)} matches, but its directory {FuzzAssert.Format(path[..separator])} is not a partial match");
                }
            }
        }
    }

    // flags (dialect and options, as glob-parse-match), structure (bits 0-5: the globs moved into a nested collection, bit 6:
    // each glob uses the next dialect), the patterns, one per line, a 0 byte, then the paths
    private static void Collection(ReadOnlySpan<byte> data)
    {
        var flags = FuzzInput.ReadByte(ref data);
        var structure = FuzzInput.ReadByte(ref data);
        var dialectIndex = flags % Dialects.Length;
        var options = (GlobOptions)((flags / Dialects.Length) % 4);
        var patterns = FuzzText.Decode(FuzzInput.ReadUntil(ref data, CollectionSeparator)).Split('\n');
        var paths = ReadPaths(data);

        var globs = new List<Glob>();
        foreach (var pattern in patterns)
        {
            if (globs.Count >= MaxCollectionGlobs)
                break;

            if (CountWildcards(pattern) > MaxWildcards)
                return;

            var dialect = (structure & 0x40) is 0 ? Dialects[dialectIndex] : Dialects[(dialectIndex + globs.Count) % Dialects.Length];
            if (Glob.TryParse(pattern, dialect, options, out var glob))
            {
                globs.Add(glob);
            }
        }

        var nested = new List<IGlobEvaluatable>();
        var items = new List<IGlobEvaluatable>();
        for (var i = 0; i < globs.Count; i++)
        {
            ((structure & (1 << i)) is 0 ? items : nested).Add(globs[i]);
        }

        if (nested.Count > 0)
        {
            items.Insert(0, new GlobCollection([.. nested]));
        }

        var collection = new GlobCollection([.. items]);
        GlobCollection fromBuilder = [.. items];
        GlobCollection reversed = [.. Enumerable.Reverse(items)];
        FuzzAssert.Equal(items.Count, collection.Count, "The collection does not hold the globs it was built from");
        for (var i = 0; i < items.Count; i++)
        {
            FuzzAssert.True(ReferenceEquals(items[i], collection[i]), $"The glob at index {i} is not the one the collection was built from");
        }

        FuzzAssert.True(items.SequenceEqual(collection), "Enumerating the collection does not give the globs it was built from");
        FuzzAssert.Equal(GetExpectedMode(collection), ((IGlobEvaluatable)collection).Mode, "The mode of the collection is not the one of its globs");

        foreach (var path in paths)
        {
            CheckCollectionMatch(collection, fromBuilder, reversed, path, "", itemType: null);
            CheckCollectionMatch(collection, fromBuilder, reversed, path, "", PathItemType.File);
            CheckCollectionMatch(collection, fromBuilder, reversed, path, "", PathItemType.Directory);

            var isPartialMatch = collection.IsPartialMatch(path);
            FuzzAssert.Equal(GetExpectedPartialMatch(collection, path), isPartialMatch, $"The collection is not a partial match of {FuzzAssert.Format(path)} exactly when one of its include globs is");
            FuzzAssert.Equal(isPartialMatch, fromBuilder.IsPartialMatch(path), $"The collection expression and the constructor disagree on the partial match of {FuzzAssert.Format(path)}");
            FuzzAssert.Equal(isPartialMatch, reversed.IsPartialMatch(path), $"The order of the globs changes the partial match of {FuzzAssert.Format(path)}");

            if (!IsSimpleRelativePath(path))
                continue;

            var index = path.LastIndexOf('/', StringComparison.Ordinal);
            if (index > 0)
            {
                CheckCollectionMatch(collection, fromBuilder, reversed, path[..index], path[(index + 1)..], itemType: null);
                CheckCollectionMatch(collection, fromBuilder, reversed, path[..index], path[(index + 1)..], PathItemType.File);
                CheckCollectionMatch(collection, fromBuilder, reversed, path[..index], path[(index + 1)..], PathItemType.Directory);
            }

            // The enumeration only recurses into the directories that are partial matches
            if (collection.IsMatch(path))
            {
                for (var separator = path.IndexOf('/', StringComparison.Ordinal); separator > 0; separator = path.IndexOf('/', separator + 1, StringComparison.Ordinal))
                {
                    FuzzAssert.True(collection.IsPartialMatch(path[..separator]), $"{FuzzAssert.Format(path)} matches, but its directory {FuzzAssert.Format(path[..separator])} is not a partial match");
                }
            }
        }
    }

    private static void CheckCollectionMatch(GlobCollection collection, GlobCollection fromBuilder, GlobCollection reversed, string directory, string filename, PathItemType? itemType)
    {
        var isMatch = collection.IsMatch(directory, filename, itemType);
        var description = $"{FuzzAssert.Format(directory)} {FuzzAssert.Format(filename)} ({FuzzAssert.Format(itemType)})";
        FuzzAssert.Equal(GetExpectedMatch(collection, directory, filename, itemType), isMatch, $"The collection does not match {description} exactly when an include glob matches it and no exclude glob does");
        FuzzAssert.Equal(isMatch, fromBuilder.IsMatch(directory, filename, itemType), $"The collection expression and the constructor disagree on {description}");
        FuzzAssert.Equal(isMatch, reversed.IsMatch(directory, filename, itemType), $"The order of the globs changes the match of {description}");
    }

    // The documented rule of a collection built from globs, applied to the results of its globs (and to a nested collection
    // by the same rule): a path matches when an include glob matches it and no exclude glob does
    private static bool GetExpectedMatch(GlobCollection collection, string directory, string filename, PathItemType? itemType)
    {
        var included = false;
        var excluded = false;
        foreach (var item in collection)
        {
            var isMatch = item is GlobCollection nested ? GetExpectedMatch(nested, directory, filename, itemType) : item.IsMatch(directory, filename, itemType);
            if (!isMatch)
                continue;

            if (GetExpectedMode(item) is GlobMode.Exclude)
            {
                excluded = true;
            }
            else
            {
                included = true;
            }
        }

        return included && !excluded;
    }

    private static bool GetExpectedPartialMatch(GlobCollection collection, string path)
    {
        foreach (var item in collection)
        {
            if (GetExpectedMode(item) is GlobMode.Exclude)
                continue;

            if (item is GlobCollection nested ? GetExpectedPartialMatch(nested, path) : item.IsPartialMatch(path))
                return true;
        }

        return false;
    }

    // A collection includes unless all its globs exclude
    private static GlobMode GetExpectedMode(IGlobEvaluatable item) => item switch
    {
        GlobCollection collection => collection.Count is 0 || collection.Any(glob => GetExpectedMode(glob) is GlobMode.Include) ? GlobMode.Include : GlobMode.Exclude,
        _ => item.Mode,
    };

    private static void GitIgnore(ReadOnlySpan<byte> data)
    {
        var contentBytes = FuzzInput.ReadUntil(ref data, GitIgnoreSeparator);
        var content = FuzzText.Decode(contentBytes);
        var paths = ReadPaths(data);

        var globs = GlobCollection.ParseGitIgnore(content);
        CheckGitIgnoreLoaders(globs, content, contentBytes);
        if (content.Split('\n').Any(line => CountWildcards(line) > MaxWildcards))
            return;

        var withCrLf = content.Contains('\r', StringComparison.Ordinal) ? null : GlobCollection.ParseGitIgnore(content.Replace("\n", "\r\n", StringComparison.Ordinal));
        var withBom = content.StartsWith('\uFEFF', StringComparison.Ordinal) ? null : GlobCollection.ParseGitIgnore("\uFEFF" + content);
        foreach (var path in paths)
        {
            _ = globs.IsMatch(path);
            _ = globs.IsPartialMatch(path);
            var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
            var itemPath = isDirectory ? path[..^1] : path;
            if (!IsSimpleRelativePath(itemPath))
                continue;

            var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;
            var index = itemPath.LastIndexOf('/', StringComparison.Ordinal);
            var directory = index < 0 ? "" : itemPath[..index];
            var name = itemPath[(index + 1)..];
            var isIgnored = globs.IsMatch(directory, name, itemType);
            FuzzAssert.Equal(isIgnored, globs.IsMatch(itemPath.AsSpan(), [], itemType), $"Splitting {FuzzAssert.Format(itemPath)} into a directory and a file name changes the match");

            if (withCrLf is not null)
            {
                FuzzAssert.Equal(isIgnored, withCrLf.IsMatch(directory, name, itemType), $"CRLF line endings change the match of {FuzzAssert.Format(path)}");
            }

            if (withBom is not null)
            {
                FuzzAssert.Equal(isIgnored, withBom.IsMatch(directory, name, itemType), $"A byte order mark changes the match of {FuzzAssert.Format(path)}");
            }

            // git cannot re-include a path whose parent directory is excluded
            if (!isIgnored)
            {
                for (var separator = itemPath.IndexOf('/', StringComparison.Ordinal); separator > 0; separator = itemPath.IndexOf('/', separator + 1, StringComparison.Ordinal))
                {
                    var ancestor = itemPath[..separator];
                    FuzzAssert.False(globs.IsMatch(ancestor.AsSpan(), [], PathItemType.Directory), $"{FuzzAssert.Format(path)} is not ignored, but its directory {FuzzAssert.Format(ancestor)} is");
                }
            }
        }
    }

    private static void CheckGitIgnoreLoaders(GlobCollection globs, string content, ReadOnlySpan<byte> contentBytes)
    {
        AssertSameGlobs(globs, GlobCollection.LoadGitIgnoreAsync(new StringReader(content)).GetAwaiter().GetResult(), "Loading the content from a TextReader");
        if (!Utf8.IsValid(contentBytes))
            return;

        AssertSameGlobs(globs, LoadFromStream([.. contentBytes]), "Loading the UTF-8 content from a Stream");

        // The byte order mark of the encoding is the one git skips, so a U+FEFF that follows it belongs to the first entry
        var withBom = GlobCollection.ParseGitIgnore("\uFEFF" + content);
        foreach (var encoding in (Encoding[])[Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.UTF32])
        {
            AssertSameGlobs(withBom, LoadFromStream([.. encoding.GetPreamble(), .. encoding.GetBytes(content)]), $"Loading the content from a Stream encoded as {encoding.WebName} with a byte order mark");
        }

        static GlobCollection LoadFromStream(byte[] bytes) => LoadFromStreamAsync(bytes).GetAwaiter().GetResult();

        static async Task<GlobCollection> LoadFromStreamAsync(byte[] bytes)
        {
            await using var stream = new MemoryStream(bytes);
            return await GlobCollection.LoadGitIgnoreAsync(stream).ConfigureAwait(false);
        }
    }

    private static void AssertSameGlobs(GlobCollection expected, GlobCollection actual, string description)
    {
        FuzzAssert.Equal(expected.Count, actual.Count, $"{description} gives a different number of globs");
        for (var i = 0; i < expected.Count; i++)
        {
            FuzzAssert.Equal(expected[i].ToString(), actual[i].ToString(), $"{description} gives a different glob at index {i}");
            FuzzAssert.Equal(expected[i].Mode, actual[i].Mode, $"{description} gives a glob of a different mode at index {i}");
        }
    }

    private static Glob? Parse(string pattern, GlobDialect dialect, GlobOptions options)
    {
        Glob? glob;
        try
        {
            glob = Glob.Parse(pattern, dialect, options);
        }
        catch (ArgumentException)
        {
            glob = null;
        }

        FuzzAssert.Equal(glob is not null, Glob.TryParse(pattern, dialect, options, out var tryParsed), "Parse and TryParse disagree");
        FuzzAssert.Equal(glob is not null, Glob.TryParse(pattern.AsSpan(), dialect, options, out _), "Parse and TryParse(ReadOnlySpan<char>) disagree");
        if (glob is not null)
        {
            FuzzAssert.Equal(glob.ToString(), tryParsed!.ToString(), "Parse and TryParse give different globs");
        }

        return glob;
    }

    private static List<string> ReadPaths(ReadOnlySpan<byte> data)
    {
        var paths = new List<string>();
        while (!data.IsEmpty && paths.Count < MaxPaths)
        {
            var path = FuzzText.Decode(FuzzInput.ReadUntil(ref data, (byte)'\n'));
            if (path.Length <= MaxPathLength)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    private static int CountWildcards(string pattern) => pattern.Count(c => c is '*');

    // A path the file system enumeration can produce: relative, without empty, '.' or '..' segments, and without '\'
    private static bool IsSimpleRelativePath(string path)
    {
        if (path.Length == 0 || path.Contains('\\', StringComparison.Ordinal))
            return false;

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
                return false;
        }

        return true;
    }

    private static IEnumerable<FuzzSeed> GetParseAndMatchSeeds()
    {
        string[] standardPatterns = ["src/**/*.txt", "**/*.cs", "*.{cs,vb}", "{a,b}/**/c?", "!**/bin/", "a/**/", "[a-c]*.md", "[!a]*", "a\\*b", "\\{a,b\\}", ".*", "**/.git/**", "a/b/c", "**", "*"];
        string[] standardPaths = ["src/a/b.txt", "a.cs", "a/b/c.cs", "a.vb", "b/x/c1", "bin/", "x/bin/", "a/b/", "b.md", ".hidden", "a*b", "{a,b}", ".git/config", "a/b/c"];
        foreach (var pattern in standardPatterns)
        {
            yield return CreateSeed("standard " + pattern, GlobDialect.Standard, GlobOptions.None, pattern, standardPaths);
            yield return CreateSeed("standard ignore-case leading-dot " + pattern, GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot, pattern, standardPaths);
        }

        // fnmatch-conformance.txt rows are "pattern<TAB>text<TAB>Posix result<TAB>PosixPath result"
        var fnmatch = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var reader = new StringReader(FuzzResources.ReadText(typeof(GlobbingFuzzTargets).Assembly, "fnmatch-conformance.txt")))
        {
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split('\t');
                if (!fnmatch.TryGetValue(parts[0], out var texts))
                {
                    texts = [];
                    fnmatch.Add(parts[0], texts);
                }

                texts.Add(parts[1]);
            }
        }

        var index = 0;
        foreach (var (pattern, texts) in fnmatch)
        {
            // The same pattern in another dialect still exercises the parser and the matcher
            var dialect = (index++ % 3) switch
            {
                0 => GlobDialect.Posix,
                1 => GlobDialect.PosixPath,
                _ => GlobDialect.Standard,
            };

            yield return CreateSeed("fnmatch " + pattern, dialect, GlobOptions.None, pattern, texts);
        }

        using var msbuild = FuzzResources.ReadJson(typeof(GlobbingFuzzTargets).Assembly, "msbuild-conformance.json");
        var files = msbuild.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToArray();
        foreach (var testCase in msbuild.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pattern = testCase.GetProperty("pattern").GetString()!;
            var matches = testCase.GetProperty("matches").EnumerateArray().Select(item => files[item.GetInt32()]);
            yield return CreateSeed("msbuild " + pattern, GlobDialect.MSBuild, GlobOptions.IgnoreCase, pattern, [.. matches.Concat(files.Take(3)).Distinct(StringComparer.Ordinal).Take(MaxPaths)]);
        }
    }

    private static FuzzSeed CreateSeed(string name, GlobDialect dialect, GlobOptions options, string pattern, IEnumerable<string> paths)
    {
        var flags = (byte)((int)dialect + (Dialects.Length * (int)options));
        return FuzzSeed.FromText(name, [flags], pattern + "\n" + string.Join('\n', paths.Take(MaxPaths)));
    }

    private static IEnumerable<FuzzSeed> GetCollectionSeeds()
    {
        string[] patterns = ["**/*.txt", "!temp/**/*", "src/**/*.cs", "!**/bin/", "*.{cs,vb}", "!*.g.cs", "a/**/", "[a-c]*.md", "!b*", "**/.git/**", ".*", "a/b/c", "**", "!**/obj/**"];
        string[] paths = ["a.txt", "temp/a.txt", "src/a/b.cs", "src/a.g.cs", "bin/", "x/bin/a.cs", "a/b/c", "b.md"];
        var index = 0;
        for (var start = 0; start < patterns.Length; start++)
        {
            var group = Enumerable.Range(start, 2 + (start % 4)).Select(i => patterns[i % patterns.Length]);
            foreach (var dialect in (GlobDialect[])[GlobDialect.Standard, GlobDialect.Git, GlobDialect.MSBuild, GlobDialect.PosixPath])
            {
                var options = (GlobOptions)(index % 4);

                // No nesting, the first and third globs nested, the second glob nested with a dialect per glob
                var structure = (index % 3) switch
                {
                    0 => 0,
                    1 => 0b101,
                    _ => 0x40 | 0b10,
                };

                var flags = (byte)((int)dialect + (Dialects.Length * (int)options));
                var text = new StringBuilder().AppendJoin('\n', group).Append('\0').AppendJoin('\n', paths).ToString();
                yield return FuzzSeed.FromText(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"collection-{index:D3}"), [flags, (byte)structure], text);
                index++;
            }
        }

        // The patterns of the MSBuild conformance cases, a few at a time
        using var msbuild = FuzzResources.ReadJson(typeof(GlobbingFuzzTargets).Assembly, "msbuild-conformance.json");
        var files = msbuild.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetString()).ToArray();
        var cases = msbuild.RootElement.GetProperty("cases").EnumerateArray().Select(testCase => testCase.GetProperty("pattern").GetString()).ToArray();
        var msbuildFlags = (byte)((int)GlobDialect.MSBuild + (Dialects.Length * (int)GlobOptions.IgnoreCase));
        for (var start = 0; start < cases.Length; start += 3)
        {
            var text = new StringBuilder().AppendJoin('\n', cases.Skip(start).Take(3)).Append('\0').AppendJoin('\n', files.Skip(start % files.Length).Take(MaxPaths)).ToString();
            yield return FuzzSeed.FromText(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"collection-msbuild-{start:D3}"), [msbuildFlags, (byte)(start % 8)], text);
        }
    }

    private static IEnumerable<FuzzSeed> GetGitIgnoreSeeds()
    {
        using var document = FuzzResources.ReadJson(typeof(GlobbingFuzzTargets).Assembly, "gitignore-conformance.json");
        var entries = document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("path").GetString() + (entry.GetProperty("directory").GetBoolean() ? "/" : ""))
            .ToArray();

        var index = 0;
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var content = testCase.GetProperty("gitignore").GetString();

            // The ignored entries, then a few others, within the number of paths a target reads
            var ignored = testCase.GetProperty("ignored").EnumerateArray().Select(item => entries[item.GetInt32()]);
            var others = Enumerable.Range(0, 4).Select(i => entries[(index + (i * 17)) % entries.Length]);
            var paths = ignored.Concat(others).Distinct(StringComparer.Ordinal).Take(MaxPaths);
            var text = new StringBuilder(content).Append('\0').AppendJoin('\n', paths).ToString();
            yield return FuzzSeed.FromText(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"gitignore-{index:D3}"), text);
            index++;
        }
    }
}
