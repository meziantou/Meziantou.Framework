using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// The known failures of the test suite (expectations.txt): one line per failing command, <c>&lt;script&gt;:&lt;line&gt;</c>,
/// with the reason after <c>#</c>. The commands not listed are expected to pass. Set the environment variable
/// <c>WASM_SPEC_UPDATE_EXPECTATIONS=1</c> to rewrite the file from the results (run a single target framework:
/// <c>-f net10.0</c>, the processes of the target frameworks run concurrently).
/// </summary>
internal static class SpecExpectations
{
    private static readonly Lock LockObject = new();
    private static SortedDictionary<string, string?>? s_entries;

    public static bool UpdateMode => Environment.GetEnvironmentVariable("WASM_SPEC_UPDATE_EXPECTATIONS") is "1" or "true";

    private static SortedDictionary<string, string?> Entries
    {
        get
        {
            if (s_entries is null)
            {
                var entries = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                if (File.Exists(SpecTestPaths.ExpectationsFile))
                {
                    foreach (var rawLine in File.ReadAllLines(SpecTestPaths.ExpectationsFile))
                    {
                        var line = rawLine.Trim();
                        if (line.Length == 0 || line[0] == '#')
                            continue;

                        var index = line.IndexOf(" # ", StringComparison.Ordinal);
                        if (index < 0)
                        {
                            entries[line] = null;
                        }
                        else
                        {
                            entries[line[..index].Trim()] = line[(index + 3)..].Trim();
                        }
                    }
                }

                s_entries = entries;
            }

            return s_entries;
        }
    }

    public static string GetKey(string script, int line) => script + ":" + line.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number of known failures of a script.</summary>
    public static int CountFailures(string script)
    {
        lock (LockObject)
        {
            return Entries.Keys.Count(key => IsKeyOf(key, script));
        }
    }

    private static bool IsKeyOf(string key, string script) => key.Length > script.Length && key.StartsWith(script, StringComparison.Ordinal) && key[script.Length] == ':';

    public static void Check(string script, IReadOnlyList<SpecFailure> failures, bool update = true)
    {
        if (UpdateMode && update)
        {
            Update(script, failures);
            return;
        }

        HashSet<string> expected;
        lock (LockObject)
        {
            expected = Entries.Keys.Where(key => IsKeyOf(key, script)).ToHashSet(StringComparer.Ordinal);
        }

        var errors = new StringBuilder();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var failure in failures)
        {
            var key = GetKey(script, failure.Line);
            if (actual.Add(key) && !expected.Contains(key))
            {
                errors.Append("REGRESSION: ").Append(key).Append(": ").AppendLine(failure.Message);
            }
        }

        foreach (var key in expected.Where(key => !actual.Contains(key)).Order(StringComparer.Ordinal))
        {
            errors.Append("UNEXPECTED PASS: remove '").Append(key).AppendLine("' from expectations.txt");
        }

        if (errors.Length > 0)
        {
            Assert.Fail(errors.ToString());
        }
    }

    private static void Update(string script, IReadOnlyList<SpecFailure> failures)
    {
        lock (LockObject)
        {
            // The reasons written by hand are kept
            var entries = Entries;
            var previous = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var key in entries.Keys.Where(key => IsKeyOf(key, script)).ToArray())
            {
                previous[key] = entries[key];
                entries.Remove(key);
            }

            foreach (var failure in failures)
            {
                var key = GetKey(script, failure.Line);
                entries.TryAdd(key, previous.TryGetValue(key, out var reason) ? reason : failure.Message.Replace('\r', ' ').Replace('\n', ' '));
            }

            var content = new StringBuilder();
            foreach (var (key, comment) in entries)
            {
                content.Append(key);
                if (comment is not null)
                {
                    content.Append(" # ").Append(comment);
                }

                content.Append('\n');
            }

            File.WriteAllText(SpecTestPaths.ExpectationsFile, content.ToString());
        }
    }
}
