using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>Finds the tests of test262, without the skipped ones and the ones that use an excluded feature.</summary>
internal static class Test262Discovery
{
    private static readonly ConcurrentDictionary<string, Lazy<DiscoveredDirectory>> Directories = new(StringComparer.Ordinal);
    private static readonly Lazy<Test262SkipList> SkipList = new(() => Test262SkipList.Load(Test262Paths.SkipFile));
    private static readonly Lazy<HashSet<string>> ExcludedFeatures = new(() => [.. ReadListFile(Test262Paths.FeaturesFile).Select(entry => entry.Value)]);

    public static Test262SkipList Skipped => SkipList.Value;

    public static IReadOnlySet<string> Excluded => ExcludedFeatures.Value;

    public sealed class DiscoveredDirectory
    {
        public required List<Test262Test> All { get; init; }

        public required List<Test262Test> Runnable { get; init; }

        public required Dictionary<string, Test262Test> ById { get; init; }

        public required Dictionary<string, Test262Test> AllById { get; init; }
    }

    /// <summary>Discovers the tests of a directory (relative to <c>test</c>), without the excluded directories.</summary>
    public static DiscoveredDirectory Discover(string directory, params string[] excludedDirectories)
    {
        return Directories.GetOrAdd(directory + "|" + string.Join('|', excludedDirectories), _ => new(() => DiscoverCore(directory, excludedDirectories))).Value;
    }

    private static DiscoveredDirectory DiscoverCore(string directory, string[] excludedDirectories)
    {
        var all = new List<Test262Test>();
        var root = Test262Paths.Tests / directory;
        if (Directory.Exists(root))
        {
            var files = Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories)
                .Select(path => (Path: path, Id: Path.GetRelativePath(Test262Paths.Tests, path).Replace('\\', '/')))
                .Where(file => !file.Id.Contains("_FIXTURE", StringComparison.Ordinal))
                .Where(file => !excludedDirectories.Any(excluded => file.Id.StartsWith(excluded.TrimEnd('/') + "/", StringComparison.Ordinal)))
                .OrderBy(file => file.Id, StringComparer.Ordinal)
                .ToList();

            var tests = new Test262Test[files.Count];
            Parallel.For(0, files.Count, i =>
            {
                var source = File.ReadAllText(files[i].Path);
                tests[i] = new Test262Test(files[i].Id, files[i].Path, Test262Metadata.Parse(source));
            });

            all.AddRange(tests);
        }

        var runnable = all.Where(test => !IsExcludedByFeature(test) && !Skipped.IsSkipped(test.Id)).ToList();
        return new DiscoveredDirectory
        {
            All = all,
            Runnable = runnable,
            ById = runnable.ToDictionary(test => test.Id, StringComparer.Ordinal),
            AllById = all.ToDictionary(test => test.Id, StringComparer.Ordinal),
        };
    }

    public static bool IsExcludedByFeature(Test262Test test) => test.Metadata.Features.Any(Excluded.Contains);

    public static TheoryData<string> GetTheoryData(string directory, params string[] excludedDirectories)
    {
        var data = new TheoryData<string>();
        if (!Test262Paths.IsAvailable)
            return data;

        // TEST262_FILTER restricts the tests to the ones whose id contains the value, or one of the values separated by '|'
        var filters = Environment.GetEnvironmentVariable("TEST262_FILTER") is { Length: > 0 } filter ? filter.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
        foreach (var test in Discover(directory, excludedDirectories).Runnable)
        {
            if (filters is not null && !Array.Exists(filters, value => test.Id.Contains(value, StringComparison.Ordinal)))
                continue;

            data.Add(test.Id);
        }

        return data;
    }

    /// <summary>Reads a list file: one entry per line, an optional reason after <c>#</c>, blank lines and comment lines ignored.</summary>
    public static List<(string Value, string? Comment)> ReadListFile(string path)
    {
        var result = new List<(string, string?)>();
        if (!File.Exists(path))
            return result;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            var commentIndex = line.IndexOf(" #", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                result.Add((line[..commentIndex].Trim(), line[(commentIndex + 2)..].Trim()));
            }
            else
            {
                result.Add((line, null));
            }
        }

        return result;
    }
}
