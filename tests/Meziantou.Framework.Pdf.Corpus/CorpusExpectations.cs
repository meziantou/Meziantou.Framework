using System.Text;

namespace Meziantou.Framework.Pdf.Corpus;

/// <summary>
/// The outcomes of the files that do not read cleanly (<c>expectations/&lt;suite&gt;.txt</c>): one line per file,
/// <c>&lt;id&gt;: &lt;exception&gt;</c>, with an optional reason after <c>#</c>. The files not listed must be read without
/// exception. Only the documented exceptions can be listed. Set the environment variable
/// <c>PDF_UPDATE_EXPECTATIONS=1</c> to rewrite the files from the results (run a single target framework: <c>-f net10.0</c>).
/// </summary>
internal static class CorpusExpectations
{
    private static readonly Lock LockObject = new();
    private static readonly Dictionary<string, SortedDictionary<string, (string Outcome, string? Comment)>> Files = new(StringComparer.Ordinal);
    private static readonly HashSet<string> DirtyFiles = new(StringComparer.Ordinal);
    private static bool s_flushRegistered;

    public static bool UpdateMode => Environment.GetEnvironmentVariable("PDF_UPDATE_EXPECTATIONS") is "1" or "true";

    public static string? GetExpectedOutcome(string id)
    {
        lock (LockObject)
        {
            return GetFile(GetSuite(id)).TryGetValue(id, out var entry) ? entry.Outcome : null;
        }
    }

    /// <summary>Checks the outcome of a file: <see langword="null"/> when it was read without exception, else the name of the exception.</summary>
    public static void Check(string id, string? outcome, string? details)
    {
        if (UpdateMode)
        {
            Update(id, outcome);
            return;
        }

        var expected = GetExpectedOutcome(id);
        if (outcome == expected)
            return;

        if (outcome is null)
        {
            Assert.Fail($"UNEXPECTED SUCCESS: remove '{id}' from expectations/{GetSuite(id)}.txt");
        }
        else
        {
            Assert.Fail($"REGRESSION: {outcome} (expected {expected ?? "no exception"})\n{details}");
        }
    }

    private static string GetSuite(string id) => id[..id.IndexOf('/', StringComparison.Ordinal)];

    private static string GetPath(string suite) => CorpusFiles.Expectations / (suite + ".txt");

    private static SortedDictionary<string, (string Outcome, string? Comment)> GetFile(string suite)
    {
        if (!Files.TryGetValue(suite, out var file))
        {
            file = new SortedDictionary<string, (string, string?)>(StringComparer.Ordinal);
            var path = GetPath(suite);
            if (File.Exists(path))
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw;
                    string? comment = null;
                    var hash = line.IndexOf(" # ", StringComparison.Ordinal);
                    if (hash >= 0)
                    {
                        comment = line[(hash + 3)..].Trim();
                        line = line[..hash];
                    }

                    var separator = line.LastIndexOf(": ", StringComparison.Ordinal);
                    if (separator > 0)
                    {
                        file[line[..separator]] = (line[(separator + 2)..].Trim(), comment);
                    }
                }
            }

            Files[suite] = file;
        }

        return file;
    }

    private static void Update(string id, string? outcome)
    {
        lock (LockObject)
        {
            var suite = GetSuite(id);
            var file = GetFile(suite);
            if (outcome is null)
            {
                file.Remove(id);
            }
            else
            {
                file[id] = (outcome, file.TryGetValue(id, out var existing) && existing.Outcome == outcome ? existing.Comment : null);
            }

            DirtyFiles.Add(suite);
            if (!s_flushRegistered)
            {
                s_flushRegistered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
            }
        }
    }

    private static void Flush()
    {
        lock (LockObject)
        {
            foreach (var suite in DirtyFiles)
            {
                var file = Files[suite];
                var path = GetPath(suite);
                if (file.Count == 0)
                {
                    File.Delete(path);
                    continue;
                }

                Directory.CreateDirectory(CorpusFiles.Expectations);
                var content = new StringBuilder();
                foreach (var (id, (outcome, comment)) in file)
                {
                    content.Append(id).Append(": ").Append(outcome);
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
}
