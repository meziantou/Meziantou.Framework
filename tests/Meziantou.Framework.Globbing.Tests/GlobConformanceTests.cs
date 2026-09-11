using System.Reflection;
using System.Text.Json;

namespace Meziantou.Framework.Globbing.Tests;

/// <summary>
/// Runs corpora whose expected results come from the reference implementation of each dialect: fnmatch(3) for the
/// POSIX dialects, git for gitignore content and MSBuild for its item specs. See <c>files/conformance.md</c> for how
/// they were produced and for the few cases where the library differs on purpose.
/// </summary>
public sealed class GlobConformanceTests
{
    [Fact]
    public void Fnmatch()
    {
        var failures = new List<string>();
        var caseCount = 0;
        using var reader = new StringReader(ReadResource("fnmatch-conformance.txt"));
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            Assert.HasCount(4, parts);

            var pattern = parts[0];
            var text = parts[1];
            Check(GlobDialect.Posix, parts[2]);

            // PosixPath splits the path on the separators of the platform, and '\' is one on Windows
            if (!OperatingSystem.IsWindows() || !text.Contains('\\', StringComparison.Ordinal))
            {
                Check(GlobDialect.PosixPath, parts[3]);
            }

            void Check(GlobDialect dialect, string expected)
            {
                if (expected is "-")
                    return;

                caseCount++;

                // An invalid pattern cannot match anything, which is what fnmatch reports for it
                if (!Glob.TryParse(pattern, dialect, GlobOptions.None, out var glob))
                {
                    if (expected is "1")
                    {
                        failures.Add($"{dialect}: '{pattern}' is rejected, but fnmatch matches '{text}'");
                    }

                    return;
                }

                if (glob.IsMatch(text) != (expected is "1"))
                {
                    failures.Add($"{dialect}: '{pattern}' on '{text}' should return {expected}");
                }
            }
        }

        Assert.True(caseCount > 4000);
        AssertNoFailure(failures);
    }

    [Fact]
    public void GitIgnore()
    {
        using var document = JsonDocument.Parse(ReadResource("gitignore-conformance.json"));
        var entries = document.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => (Path: entry.GetProperty("path").GetString()!, IsDirectory: entry.GetProperty("directory").GetBoolean()))
            .ToArray();

        var failures = new List<string>();
        var caseCount = 0;
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var content = testCase.GetProperty("gitignore").GetString()!;
            var ignored = testCase.GetProperty("ignored").EnumerateArray().Select(index => index.GetInt32()).ToHashSet();
            var globs = GlobCollection.ParseGitIgnore(content);
            caseCount++;

            for (var i = 0; i < entries.Length; i++)
            {
                var (path, isDirectory) = entries[i];
                var index = path.AsSpan().LastIndexOf('/');
                var directory = path.AsSpan(0, Math.Max(index, 0));
                var name = path.AsSpan(index + 1);

                var expected = ignored.Contains(i);
                if (globs.IsMatch(directory, name, isDirectory ? PathItemType.Directory : PathItemType.File) != expected)
                {
                    failures.Add($"{JsonSerializer.Serialize(content)}: '{path}'{(isDirectory ? " (directory)" : "")} should {(expected ? "" : "not ")}be ignored");
                }
            }
        }

        Assert.True(caseCount > 500);
        AssertNoFailure(failures);
    }

    [Fact]
    public void MSBuild()
    {
        using var document = JsonDocument.Parse(ReadResource("msbuild-conformance.json"));
        var files = document.RootElement.GetProperty("files").EnumerateArray().Select(file => file.GetString()!).ToArray();

        var failures = new List<string>();
        var caseCount = 0;
        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pattern = testCase.GetProperty("pattern").GetString()!;
            var matches = testCase.GetProperty("matches").EnumerateArray().Select(index => index.GetInt32()).ToHashSet();
            var glob = Glob.Parse(pattern, GlobDialect.MSBuild, GlobOptions.IgnoreCase);
            caseCount++;

            for (var i = 0; i < files.Length; i++)
            {
                var expected = matches.Contains(i);
                if (glob.IsMatch(files[i]) != expected)
                {
                    failures.Add($"'{pattern}' should {(expected ? "" : "not ")}include '{files[i]}'");
                }
            }
        }

        Assert.True(caseCount > 500);
        AssertNoFailure(failures);
    }

    private static void AssertNoFailure(List<string> failures)
    {
        if (failures.Count > 0)
        {
            Assert.Fail($"{failures.Count} results differ from the reference implementation:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(50))}");
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(GlobConformanceTests).GetTypeInfo().Assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
