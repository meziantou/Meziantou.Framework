using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// The known failures of the test suite (expectations.txt): one line per failing command, <c>&lt;script&gt;:&lt;line&gt;</c>,
/// with the reason after <c>#</c>. The commands not listed are expected to pass. Set the environment variable
/// <c>WASM_SPEC_UPDATE_EXPECTATIONS=1</c> to rewrite the file from the results (run a single target framework:
/// <c>-f net10.0</c>, the processes of the target frameworks run concurrently).
/// </summary>
internal sealed class SpecExpectations(FullPath file)
{
    private readonly Lock _lock = new();
    private SortedDictionary<string, string?>? _entries;

    /// <summary>The known failures of the core test suite (expectations.txt).</summary>
    public static SpecExpectations Core { get; } = new(SpecTestPaths.ExpectationsFile);

    /// <summary>The known failures of the component model test suite (component-expectations.txt).</summary>
    public static SpecExpectations Components { get; } = new(SpecTestPaths.ComponentExpectationsFile);

    public static bool UpdateMode => Environment.GetEnvironmentVariable("WASM_SPEC_UPDATE_EXPECTATIONS") is "1" or "true";

    private SortedDictionary<string, string?> Entries
    {
        get
        {
            if (_entries is null)
            {
                var entries = new SortedDictionary<string, string?>(StringComparer.Ordinal);
                if (File.Exists(file))
                {
                    foreach (var rawLine in File.ReadAllLines(file))
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

                _entries = entries;
            }

            return _entries;
        }
    }

    public static string GetKey(string script, int line) => script + ":" + line.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number of known failures of a script.</summary>
    public int CountFailures(string script)
    {
        lock (_lock)
        {
            return Entries.Keys.Count(key => IsKeyOf(key, script));
        }
    }

    private static bool IsKeyOf(string key, string script) => key.Length > script.Length && key.StartsWith(script, StringComparison.Ordinal) && key[script.Length] == ':';

    public void Check(string script, IReadOnlyList<SpecFailure> failures, bool update = true)
    {
        if (UpdateMode && update)
        {
            Update(script, failures);
            return;
        }

        HashSet<string> expected;
        lock (_lock)
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
            errors.Append("UNEXPECTED PASS: remove '").Append(key).Append("' from ").AppendLine(file.Name);
        }

        if (errors.Length > 0)
        {
            Assert.Fail(errors.ToString());
        }
    }

    private void Update(string script, IReadOnlyList<SpecFailure> failures)
    {
        lock (_lock)
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

            File.WriteAllText(file, content.ToString());
        }
    }
}
