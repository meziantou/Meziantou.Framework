using System.Text;

namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>
/// The known failures of test262 (expectations/&lt;area&gt;.txt): one line per failing run, <c>&lt;id&gt;:&lt;mode&gt;</c>, with an
/// optional reason after <c>#</c>. The runs not listed are expected to pass. Set the environment variable
/// <c>TEST262_UPDATE_EXPECTATIONS=1</c> to rewrite the files from the results (run a single target framework:
/// <c>-f net10.0</c>, the processes of the target frameworks run concurrently).
/// </summary>
internal static class Test262Expectations
{
    private static readonly Lock LockObject = new();
    private static readonly Dictionary<string, SortedDictionary<string, string?>> Files = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DirtyFiles = new(StringComparer.Ordinal);
    private static bool s_flushRegistered;
    private static Timer? s_flushTimer;

    public static bool UpdateMode => Environment.GetEnvironmentVariable("TEST262_UPDATE_EXPECTATIONS") is "1" or "true";

    /// <summary>The area of a test: its first two path segments (built-ins/Array \u2192 built-ins-Array).</summary>
    public static string GetArea(string id)
    {
        var segments = id.Split('/');
        return segments.Length > 2 ? segments[0] + "-" + segments[1] : segments[0];
    }

    public static string GetKey(string id, Test262Mode mode) => id + ":" + mode.ToString().ToLowerInvariant();

    private static SortedDictionary<string, string?> GetFile(string area)
    {
        if (!Files.TryGetValue(area, out var file))
        {
            file = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (value, comment) in Test262Discovery.ReadListFile(GetPath(area)))
            {
                file[value] = comment;
            }

            Files[area] = file;
        }

        return file;
    }

    private static FullPath GetPath(string area) => Test262Paths.Expectations / (area + ".txt");

    public static bool IsExpectedToFail(string id, Test262Mode mode)
    {
        lock (LockObject)
        {
            return GetFile(GetArea(id)).ContainsKey(GetKey(id, mode));
        }
    }

    /// <summary>The number of known failures of an area.</summary>
    public static int CountFailures(string areaPrefix)
    {
        lock (LockObject)
        {
            var count = 0;
            if (!Directory.Exists(Test262Paths.Expectations))
                return 0;

            foreach (var path in Directory.EnumerateFiles(Test262Paths.Expectations, "*.txt"))
            {
                var file = GetFile(Path.GetFileNameWithoutExtension(path));
                count += file.Keys.Count(key => key.StartsWith(areaPrefix, StringComparison.Ordinal));
            }

            return count;
        }
    }

    public static void Check(Test262Test test, IReadOnlyList<Test262Result> results)
    {
        if (UpdateMode)
        {
            Update(test, results);
            return;
        }

        var errors = new StringBuilder();
        foreach (var result in results)
        {
            var expectedToFail = IsExpectedToFail(test.Id, result.Mode);
            if (result.Passed && expectedToFail)
            {
                errors.AppendLine($"UNEXPECTED PASS ({result.Mode}): remove '{GetKey(test.Id, result.Mode)}' from expectations/{GetArea(test.Id)}.txt");
            }
            else if (!result.Passed && !expectedToFail)
            {
                errors.AppendLine($"REGRESSION ({result.Mode}): {result.Message}");
            }
        }

        if (errors.Length > 0)
        {
            errors.AppendLine().Append("Test: ").AppendLine(test.FullPath);
            Assert.Fail(errors.ToString());
        }
    }

    private static void Update(Test262Test test, IReadOnlyList<Test262Result> results)
    {
        lock (LockObject)
        {
            var area = GetArea(test.Id);
            var file = GetFile(area);
            foreach (var mode in Enum.GetValues<Test262Mode>())
            {
                var key = GetKey(test.Id, mode);
                var result = results.FirstOrDefault(r => r.Mode == mode);
                if (result is null || result.Passed)
                {
                    file.Remove(key);
                }
                else if (!file.ContainsKey(key))
                {
                    file[key] = null;
                }
            }

            DirtyFiles.Add(area);
            if (!s_flushRegistered)
            {
                s_flushRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    s_flushTimer?.Dispose();
                    Flush();
                    Test262Summary.Write();
                };
                s_flushTimer = new Timer(static _ => Flush(), state: null, dueTime: 2000, period: 2000);
            }
        }
    }

    private static void Flush()
    {
        lock (LockObject)
        {
            foreach (var area in DirtyFiles)
            {
                var file = Files[area];
                var path = GetPath(area);
                if (file.Count == 0)
                {
                    File.Delete(path);
                    continue;
                }

                Directory.CreateDirectory(Test262Paths.Expectations);
                var content = new StringBuilder();
                foreach (var (key, comment) in file)
                {
                    content.Append(key);
                    if (comment is not null)
                    {
                        content.Append(" # ").Append(comment);
                    }

                    content.Append('\n');
                }

                File.WriteAllText(path, content.ToString());
            }

            DirtyFiles.Clear();
        }
    }

    /// <summary>Forgets the files read so far, so that the next reads see the files on disk.</summary>
    internal static void Reset()
    {
        lock (LockObject)
        {
            Files.Clear();
        }
    }
}
