using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// The known failures of the WASI test suite (wasi-expectations.txt): one line per failing test and mode,
/// <c>&lt;mode&gt;:&lt;language&gt;/&lt;test&gt;</c>, with the reason after <c>#</c>. The tests not listed are expected to
/// pass. Set the environment variable <c>WASM_SPEC_UPDATE_EXPECTATIONS=1</c> to rewrite the file from the results (run a
/// single target framework: <c>-f net10.0</c>).
/// </summary>
internal static class WasiExpectations
{
    private static readonly Lock LockObject = new();
    private static SortedDictionary<string, string?>? s_entries;

    public static FullPath ExpectationsFile { get; } = SpecTestPaths.Project / "wasi-expectations.txt";

    private static SortedDictionary<string, string?> Entries
    {
        get
        {
            if (s_entries is null)
            {
                var entries = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                if (File.Exists(ExpectationsFile))
                {
                    foreach (var rawLine in File.ReadAllLines(ExpectationsFile))
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

    public static string GetKey(string test, WasiMode mode) => mode.ToString().ToLowerInvariant() + ":" + test;

    /// <summary>The known failures, with their reasons.</summary>
    public static IReadOnlyList<(string Key, string? Reason)> GetFailures()
    {
        lock (LockObject)
        {
            return Entries.Select(entry => (entry.Key, entry.Value)).ToArray();
        }
    }

    public static bool IsExpectedToFail(string test, WasiMode mode)
    {
        lock (LockObject)
        {
            return Entries.ContainsKey(GetKey(test, mode));
        }
    }

    public static void Check(string test, WasiMode mode, string? failure)
    {
        var key = GetKey(test, mode);
        if (SpecExpectations.UpdateMode)
        {
            Update(key, failure);
            return;
        }

        var expected = IsExpectedToFail(test, mode);
        if (failure is not null && !expected)
        {
            Assert.Fail("REGRESSION: " + key + ": " + failure);
        }

        if (failure is null && expected)
        {
            Assert.Fail("UNEXPECTED PASS: remove '" + key + "' from wasi-expectations.txt");
        }
    }

    private static void Update(string key, string? failure)
    {
        lock (LockObject)
        {
            // The reasons written by hand are kept
            var entries = Entries;
            if (failure is null)
            {
                entries.Remove(key);
            }
            else if (!entries.ContainsKey(key))
            {
                entries[key] = failure.Replace('\r', ' ').Replace('\n', ' ');
            }

            var content = new StringBuilder();
            foreach (var (entryKey, comment) in entries)
            {
                content.Append(entryKey);
                if (comment is not null)
                {
                    content.Append(" # ").Append(comment);
                }

                content.Append('\n');
            }

            File.WriteAllText(ExpectationsFile, content.ToString());
        }
    }

    public static string FormatRate(int passing, int total) => (total == 0 ? 100.0 : passing * 100.0 / total).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}
